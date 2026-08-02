using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Ретро-сверка загруженных протоколов (см. <see cref="IImportAuditService"/>).
///
/// День файла сопоставляется с соревнованием в БД **по дате внутри события**, а не по имени:
/// имена как раз и разъезжаются («…חלק ב'»), из-за чего переимпорт однажды создал полный
/// дубликат (инцидент И-3). Внутри одного события даты дней уникальны, так что это надёжнее
/// — и заодно репетиция фазы Д2.
/// </summary>
public class ImportAuditService(
    SwimmDbContext db,
    ICompetitionDiscoveryProvider discovery,
    IResultSourceProvider sourceProvider) : IImportAuditService
{
    public async Task<IReadOnlyList<ImportAuditReport>> AuditAllAsync(int? limit = null, CancellationToken ct = default)
    {
        var candidates = await db.DiscoveredCompetitions.AsNoTracking()
            .Where(d => d.LogligId != null && d.Status == "imported")
            .OrderBy(d => d.DateStart)
            .Select(d => d.Id)
            .ToListAsync(ct);

        if (limit is int max) candidates = candidates.Take(max).ToList();

        var reports = new List<ImportAuditReport>();
        foreach (var id in candidates)
            reports.Add(await AuditDiscoveredAsync(id, ct));

        return reports;
    }

    public async Task<ImportAuditReport> AuditDiscoveredAsync(int discoveredId, CancellationToken ct = default)
    {
        var row = await db.DiscoveredCompetitions.AsNoTracking().FirstOrDefaultAsync(d => d.Id == discoveredId, ct);
        if (row == null)
            return new ImportAuditReport(discoveredId, 0, "", $"Запись #{discoveredId} не найдена", []);
        if (row.LogligId is not int logligId)
            return new ImportAuditReport(discoveredId, row.OrgCompId, row.Name, "Нет LogligId — источник неизвестен", []);

        // 1. Файл: качаем и парсим ТЕКУЩИМ парсером — в этом весь смысл аудита.
        ParsedCompetition parsed;
        try
        {
            var pdf = await discovery.FetchResultsPdfAsync(logligId, "he-IL", ct);
            using var ms = new MemoryStream(pdf);
            parsed = await sourceProvider.ParseAsync(new ResultSourceRequest(
                ms, $"audit-{logligId}-he.pdf", "IsrOrg", Language: "he"));
        }
        catch (Exception ex)
        {
            return new ImportAuditReport(discoveredId, row.OrgCompId, row.Name,
                $"Файл не разобран: {ex.GetType().Name}: {ex.Message}", []);
        }

        var items = JsonSerializer.Deserialize<List<AuditItem>>(parsed.ResultsJson) ?? [];
        if (items.Count == 0)
            return new ImportAuditReport(discoveredId, row.OrgCompId, row.Name, "Файл распознан, но строк нет", []);

        // 2. Дни БД — тем же резолвом, что и импорт (CompetitionIdentity): иначе аудит
        // отчитывался бы про одни дни, а переимпорт писал в другие.
        var dbDays = await CompetitionIdentity.ResolveDaysAsync(db, row.OrgCompId, ct);
        if (dbDays.Count == 0)
            return new ImportAuditReport(discoveredId, row.OrgCompId, row.Name,
                "В БД нет соревнования со штампом этого compID — сверять не с чем", []);

        var dbByDate = ImportCompetitionMatcher.BuildDateIndex(dbDays, c => c.Date);

        // 3. Ожидаемое из файла — той же функцией, что штатная сверка импорта.
        var expectedByDay = items
            .GroupBy(i => i.Date ?? "")
            .ToDictionary(
                g => g.Key,
                // Ключ БЕЗ категории: EventCategory появилась 2026-07-28, у старых импортов
                // её нет — полный ключ объявил бы их сплошным расхождением (см. EventKeyCoarse).
                g => g.GroupBy(i => ImportReconciler.EventKeyCoarse(
                        JsonImportService.NormalizeStyleName(i.EventStyleName),
                        i.EventStyleLen, i.IsRelay == true))
                      .ToDictionary(x => x.Key, x => x.Count()));

        var days = new List<ImportAuditDay>();
        var toPersist = new List<ImportReconciliation>();
        var stamp = DateTime.UtcNow;

        foreach (var (date, expectedEvents) in expectedByDay.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (!dbByDate.TryGetValue(date, out var comp))
            {
                days.Add(new ImportAuditDay(date, null, "", expectedEvents.Values.Sum(), 0, []));
                continue;
            }

            var actualEvents = (await db.Results.AsNoTracking()
                    .Where(r => r.CompetitionId == comp.Id)
                    .Select(r => new
                    {
                        StyleName = r.Style != null ? r.Style.Name : "",
                        r.Distance,
                        IsRelay = r.RelayId != null
                    })
                    .ToListAsync(ct))
                .GroupBy(r => ImportReconciler.EventKeyCoarse(r.StyleName, r.Distance, r.IsRelay))
                .ToDictionary(g => g.Key, g => g.Count());

            var rows = ImportReconciler.Build(
                expectedEvents.ToDictionary(kv => (comp.Id, kv.Key), kv => kv.Value),
                actualEvents.ToDictionary(kv => (comp.Id, kv.Key), kv => kv.Value));

            toPersist.AddRange(rows.Select(r => new ImportReconciliation
            {
                CompetitionId = r.CompetitionId,
                ImportedAt = stamp,
                // Пометка аудита: строку сверки породил не импорт, а ретро-прогон.
                ImportFileName = $"audit:loglig-{logligId}-he.pdf",
                EventKey = r.EventKey,
                ExpectedRows = r.Expected,
                ActualRows = r.Actual,
                Status = r.Status
            }));

            var total = rows.Single(r => r.EventKey.Length == 0);
            days.Add(new ImportAuditDay(
                date, comp.Id, comp.Name, total.Expected, total.Actual,
                rows.Where(r => r.EventKey.Length > 0 && r.IsMismatch)
                    .Select(r => new ImportAuditEventDiff(r.CompetitionId, r.EventKey, r.Expected, r.Actual))
                    .ToList()));
        }

        if (toPersist.Count > 0)
        {
            db.ImportReconciliations.AddRange(toPersist);
            await db.SaveChangesAsync(ct);
        }

        return new ImportAuditReport(discoveredId, row.OrgCompId, row.Name, null, days);
    }

    /// <summary>Минимум полей строки результата, нужный сверке (полный разбор — дело импорта).</summary>
    private sealed class AuditItem
    {
        [System.Text.Json.Serialization.JsonPropertyName("date")]
        public string? Date { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("event_style_name")]
        public string? EventStyleName { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("event_style_len")]
        public string? EventStyleLen { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("event_category")]
        public string? EventCategory { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("is_relay")]
        public bool? IsRelay { get; set; }
    }
}
