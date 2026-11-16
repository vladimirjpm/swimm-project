using System.Net;

namespace Swimm.Parsing.RecordSources;

/// <summary>
/// Скачивание одного файла World Aquatics с повторами — общее для прогона по странам
/// (<see cref="WorldAquaticsCountryFetcher"/>) и мировых рекордов
/// (<see cref="WorldRecordsSourceProvider"/>).
///
/// Повторяем только то, что может пройти само: обрыв сети (в том числе сбой DNS), таймаут,
/// 5xx и 429. Шлюз api.worldaquatics.com регулярно режет долгие отчёты 504-м — 16.11.2026
/// проверка источников на проде дважды подряд упала на worldrecords именно так, хотя через
/// пять минут тот же запрос проходил. На 4xx повтор бессмысленен: кривой запрос лучше
/// увидеть сразу.
///
/// Повторяется ОДИН файл, а не весь набор: уже скачанные отчёты второй раз не тянем.
/// </summary>
public static class WorldAquaticsDownload
{
    /// <summary>Попыток на файл: первая + два повтора.</summary>
    public const int MaxAttempts = 3;

    /// <summary>
    /// Дольше этого по <c>Retry-After</c> не ждём: источник просит 300 с (замер 16.11.2026), и
    /// запас сверху — на случай, если он поднимет. Просьба дольше — ждём столько и пробуем.
    /// </summary>
    public static readonly TimeSpan MaxRetryAfter = TimeSpan.FromMinutes(6);

    /// <summary>Ожидание между попытками — подменяется в тестах, чтобы не спать пять минут.</summary>
    public delegate Task Delay(TimeSpan wait, CancellationToken ct);

    /// <param name="what">Что качаем — для текста ошибки: «WR SCM», «NR LCM для AGU».</param>
    /// <param name="retryDelay">Пауза между попытками; в тестах — ноль.</param>
    /// <param name="delay">Чем ждать; null — <see cref="Task.Delay(TimeSpan, CancellationToken)"/>.</param>
    public static async Task<MemoryStream> DownloadAsync(
        HttpClient client, Uri url, string what, TimeSpan retryDelay, CancellationToken ct, Delay? delay = null)
    {
        delay ??= Task.Delay;

        for (var attempt = 1; ; attempt++)
        {
            var last = attempt >= MaxAttempts;
            try
            {
                var response = await client.GetAsync(attempt == 1 ? url : CacheBusted(url, attempt), ct);

                if (IsTransient(response.StatusCode))
                {
                    var wait = WaitBeforeRetry(response, retryDelay);
                    if (last)
                        throw new InvalidOperationException(
                            $"{what}: источник отвечает {(int)response.StatusCode} после {MaxAttempts} попыток"
                            + (response.StatusCode == HttpStatusCode.TooManyRequests
                                ? $" (лимит частоты, ждали по Retry-After до {wait.TotalSeconds:0} с)."
                                : "."));

                    await delay(wait, ct);
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                    throw new InvalidOperationException(
                        $"{what}: источник ответил {(int)response.StatusCode} {response.ReasonPhrase}.");

                var ms = new MemoryStream();
                await response.Content.CopyToAsync(ms, ct);
                ms.Position = 0;
                return ms;
            }
            catch (Exception ex) when (!last && IsRetriable(ex, ct))
            {
                await delay(retryDelay, ct);
            }
            catch (Exception ex) when (last && IsRetriable(ex, ct))
            {
                throw new InvalidOperationException(
                    $"{what} не скачался за {MaxAttempts} попытки: {ex.Message}", ex);
            }
        }
    }

    /// <summary>
    /// Повтор — мимо кэша CloudFront: он кэширует 504 (<c>X-Cache: Error from cloudfront</c>,
    /// замер 16.11.2026), и повтор того же URL в ближайшие секунды получает ту же ошибку за 15 мс,
    /// не дойдя до источника. Лишний параметр query источник игнорирует (проверено на отчёте и
    /// на JSON-выдаче); хост и путь не меняются — whitelist домена в силе.
    /// </summary>
    public static Uri CacheBusted(Uri url, int attempt)
    {
        var sep = string.IsNullOrEmpty(url.Query) ? "?" : "&";
        return new Uri($"{url.AbsoluteUri}{sep}_retry={attempt}-{DateTime.UtcNow.Ticks}");
    }

    /// <summary>
    /// Сколько ждать перед повтором. 429 с <c>Retry-After</c> — столько, сколько просит источник
    /// (не больше <see cref="MaxRetryAfter"/>): повтор через 2 с, пока лимит действует, уходит
    /// впустую и сам добавляет запросов. 16.11.2026 так за вечер дважды пропала половина стран
    /// прогона — каждая падала за секунды, не дождавшись конца пятиминутного окна (И-67).
    /// </summary>
    public static TimeSpan WaitBeforeRetry(HttpResponseMessage response, TimeSpan retryDelay)
    {
        if (response.StatusCode != HttpStatusCode.TooManyRequests) return retryDelay;

        var header = response.Headers.RetryAfter;
        var asked = header?.Delta
                    ?? (header?.Date is { } date ? date - DateTimeOffset.UtcNow : (TimeSpan?)null);
        if (asked is not { } wait || wait <= retryDelay) return retryDelay;

        return wait > MaxRetryAfter ? MaxRetryAfter : wait;
    }

    private static bool IsTransient(HttpStatusCode status) =>
        (int)status >= 500 || status == HttpStatusCode.TooManyRequests;

    /// <summary>
    /// Таймаут клиента приходит как <see cref="TaskCanceledException"/> — тем же типом, что и
    /// отмена вызывающим. Различаем по токену: отмену повторять нельзя. Сбой DNS («No such
    /// host is known») — <see cref="HttpRequestException"/>, повторяем.
    /// </summary>
    private static bool IsRetriable(Exception ex, CancellationToken ct) =>
        ex is HttpRequestException
        || (ex is TaskCanceledException && !ct.IsCancellationRequested);
}
