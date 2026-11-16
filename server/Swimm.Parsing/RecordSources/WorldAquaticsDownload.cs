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

    /// <param name="what">Что качаем — для текста ошибки: «WR SCM», «NR LCM для AGU».</param>
    /// <param name="retryDelay">Пауза между попытками; в тестах — ноль.</param>
    public static async Task<MemoryStream> DownloadAsync(
        HttpClient client, Uri url, string what, TimeSpan retryDelay, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            var last = attempt >= MaxAttempts;
            try
            {
                var response = await client.GetAsync(url, ct);

                if (IsTransient(response.StatusCode))
                {
                    if (last)
                        throw new InvalidOperationException(
                            $"{what}: источник отвечает {(int)response.StatusCode} после {MaxAttempts} попыток.");

                    await Task.Delay(retryDelay, ct);
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
                await Task.Delay(retryDelay, ct);
            }
            catch (Exception ex) when (last && IsRetriable(ex, ct))
            {
                throw new InvalidOperationException(
                    $"{what} не скачался за {MaxAttempts} попытки: {ex.Message}", ex);
            }
        }
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
