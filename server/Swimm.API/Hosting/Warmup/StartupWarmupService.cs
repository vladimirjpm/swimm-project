using System.Diagnostics;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using OpenTelemetry;
using Swimm.API.Telemetry;

namespace Swimm.API.Hosting.Warmup;

/// <summary>
/// Прогрев после старта (решение Влада 19.11.2026): один раз, сразу как Kestrel начал принимать
/// запросы, сайт сам открывает <see cref="WarmupPaths"/> — настоящими HTTP-запросами к себе, чтобы
/// пройти тот же путь, что посетитель (middleware, контроллер, ключ кэша). Без этого первый человек
/// после каждой выкатки ждал до 17–28 с (телеметрия 18.11: 82 из 125 запросов дольше 3 с — первые
/// 10 минут после старта).
///
/// Только прод (<c>Warmup:Enabled</c> в <c>appsettings.Production.json</c>): локально процесс
/// перезапускают постоянно, а ProdAdmin грел бы прод-базу зря. Запросы идут на loopback с заголовками
/// <c>Host</c> = канонический хост (иначе редирект на swimhub.co.il), <c>X-Forwarded-Proto: https</c>
/// (иначе редирект на https) и <see cref="RequestSource.WarmupHeader"/> — в телеметрии они помечены
/// <c>swimm.source=warmup</c> и в p95 посетителей не входят. Свои исходящие спаны HttpClient подавлены.
/// Итог — строкой в журнал: Information (в Azure не уходит), а если что-то не прошло — Warning.
/// Карточки на /Admin/Jobs нет намеренно: это не служба по расписанию, а разовый шаг старта на проде.
/// </summary>
internal sealed class StartupWarmupService(
    IServer server,
    IHostApplicationLifetime lifetime,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<StartupWarmupService> logger) : BackgroundService
{
    public const string HttpClientName = "warmup";
    private static readonly TimeSpan PerRequestTimeout = TimeSpan.FromSeconds(60);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!await WaitForStartAsync(stoppingToken)) return;

        var baseUri = LoopbackBase(server.Features.Get<IServerAddressesFeature>()?.Addresses);
        if (baseUri is null)
        {
            logger.LogWarning("Прогрев после старта пропущен: не нашёл адрес http, на котором слушает Kestrel");
            return;
        }

        var host = CanonicalHost(configuration);
        var paths = configuration.GetSection(WarmupPaths.PathsKey).Get<string[]>() is { Length: > 0 } custom
            ? custom
            : WarmupPaths.Default;

        using var suppress = SuppressInstrumentationScope.Begin();
        var client = httpClientFactory.CreateClient(HttpClientName);
        client.Timeout = PerRequestTimeout;

        var total = Stopwatch.StartNew();
        var failed = new List<string>();
        foreach (var path in paths)
        {
            if (stoppingToken.IsCancellationRequested) return;
            var outcome = await SendAsync(client, baseUri, path, host, stoppingToken);
            if (outcome is not null) failed.Add($"{path} → {outcome}");
        }

        if (failed.Count > 0)
            logger.LogWarning("Прогрев после старта: {Failed} из {Total} адресов не прошли за {Ms} мс: {List}",
                failed.Count, paths.Length, total.ElapsedMilliseconds, string.Join("; ", failed));
        else
            logger.LogInformation("Прогрев после старта: {Total} адресов за {Ms} мс", paths.Length, total.ElapsedMilliseconds);
    }

    /// <summary>null — прошёл; иначе — что случилось (код 5xx или исключение).</summary>
    private static async Task<string?> SendAsync(HttpClient client, Uri baseUri, string path, string? host, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUri, path));
            if (host is not null) request.Headers.Host = host;
            request.Headers.Add("X-Forwarded-Proto", "https");
            request.Headers.Add(RequestSource.WarmupHeader, "1");
            using var response = await client.SendAsync(request, ct);
            return (int)response.StatusCode >= 500 ? ((int)response.StatusCode).ToString() : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return ex.GetType().Name;
        }
    }

    private async Task<bool> WaitForStartAsync(CancellationToken ct)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var onStart = lifetime.ApplicationStarted.Register(() => started.TrySetResult());
        using var onStop = ct.Register(() => started.TrySetCanceled(ct));
        try { await started.Task; return true; }
        catch (OperationCanceledException) { return false; }
    }

    /// <summary>
    /// Адрес для запросов к себе: первый http из адресов Kestrel, хост «везде» (<c>+</c>, <c>*</c>,
    /// <c>0.0.0.0</c>, <c>[::]</c>) заменён на 127.0.0.1. На App Service Linux это <c>http://+:8080</c>.
    /// </summary>
    internal static Uri? LoopbackBase(IEnumerable<string>? addresses)
    {
        foreach (var raw in addresses ?? [])
        {
            if (!raw.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) continue;
            var normalized = raw
                .Replace("://+:", "://127.0.0.1:")
                .Replace("://*:", "://127.0.0.1:")
                .Replace("://0.0.0.0:", "://127.0.0.1:")
                .Replace("://[::]:", "://127.0.0.1:");
            if (Uri.TryCreate(normalized, UriKind.Absolute, out var uri))
                return new Uri($"{uri.Scheme}://{uri.Host}:{uri.Port}/");
        }
        return null;
    }

    private static string? CanonicalHost(IConfiguration config)
    {
        var baseUrl = config[SeoPages.BaseUrlKey] ?? SeoPages.DefaultBaseUrl;
        return Uri.TryCreate(baseUrl, UriKind.Absolute, out var b) ? b.Host : null;
    }
}
