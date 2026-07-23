using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// On-demand проверка живости ссылок Sys_UserMedia (фаза 7.5). Логика по SourceType:
/// youtube/vimeo — oEmbed (200 = жив, 404/400 = битый), other/image — сама ссылка с
/// ResponseHeadersRead (2xx/3xx = жив, 4xx/5xx/сетевая ошибка = битый). Параллелизм HTTP
/// ограничен SemaphoreSlim(6); запись в DbContext — последовательно, после сбора результатов
/// (EF DbContext не потокобезопасен).
/// </summary>
public class UserMediaLinkChecker(
    SwimmDbContext db,
    IHttpClientFactory httpFactory,
    ILogger<UserMediaLinkChecker> logger) : IUserMediaLinkChecker
{
    private const string HttpClientName = "media-link-check";

    private sealed record CheckOutcome(int MediaId, bool Ok, int? StatusCode, string? Error);

    public async Task<UserMediaLinkCheckReport> CheckAllAsync(CancellationToken ct = default)
    {
        var rows = await db.UserMedia.ToListAsync(ct);

        var client = httpFactory.CreateClient(HttpClientName);
        var semaphore = new SemaphoreSlim(6);

        var tasks = rows.Select(async row =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                return await CheckOneAsync(client, row, ct);
            }
            finally
            {
                semaphore.Release();
            }
        });

        var outcomes = await Task.WhenAll(tasks);

        // Запись в DbContext — последовательно, после того как все HTTP-запросы отработали.
        var now = DateTime.UtcNow;
        var byId = rows.ToDictionary(r => r.Id);
        foreach (var outcome in outcomes)
        {
            var row = byId[outcome.MediaId];
            row.LinkCheckedAt = now;
            row.LinkOk = outcome.Ok;
            row.LinkStatusCode = outcome.StatusCode;
            row.LinkError = outcome.Error;
        }

        await db.SaveChangesAsync(ct);

        var okCount = outcomes.Count(o => o.Ok);
        return new UserMediaLinkCheckReport(rows.Count, okCount, rows.Count - okCount);
    }

    private async Task<CheckOutcome> CheckOneAsync(HttpClient client, UserMedia row, CancellationToken ct)
    {
        try
        {
            var checkUrl = BuildCheckUrl(row);
            using var response = await client.GetAsync(checkUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            var statusCode = (int)response.StatusCode;

            bool ok = row.SourceType switch
            {
                "youtube" or "vimeo" => response.StatusCode == HttpStatusCode.OK,
                _ => (int)response.StatusCode is >= 200 and < 400,
            };

            string? error = ok ? null : Truncate($"HTTP {statusCode}");
            return new CheckOutcome(row.Id, ok, statusCode, error);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            logger.LogWarning(ex, "Проверка ссылки медиа #{Id} упала: {Url}", row.Id, row.Url);
            return new CheckOutcome(row.Id, false, null, Truncate($"{ex.GetType().Name}: {ex.Message}"));
        }
    }

    private static string BuildCheckUrl(UserMedia row) => row.SourceType switch
    {
        "youtube" => $"https://www.youtube.com/oembed?format=json&url={Uri.EscapeDataString(row.Url)}",
        "vimeo" => $"https://vimeo.com/api/oembed.json?url={Uri.EscapeDataString(row.Url)}",
        _ => row.Url,
    };

    private static string Truncate(string s) => s.Length <= 200 ? s : s[..200];

    public async Task<IReadOnlyList<BrokenMediaRowDto>> GetBrokenAsync(CancellationToken ct = default)
    {
        return await db.UserMedia.AsNoTracking()
            .Where(m => m.LinkOk == false)
            .Include(m => m.User)
            .Include(m => m.Swimmer)
            .OrderByDescending(m => m.LinkCheckedAt)
            .Select(m => new BrokenMediaRowDto(
                m.Id, m.Url, m.User.Email, m.Swimmer.LastName + " " + m.Swimmer.FirstName,
                m.MediaType, m.SourceType,
                m.LinkCheckedAt, m.LinkStatusCode, m.LinkError))
            .ToListAsync(ct);
    }
}
