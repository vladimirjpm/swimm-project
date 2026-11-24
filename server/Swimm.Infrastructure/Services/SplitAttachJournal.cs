using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Mapping;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Итог последней доклейки промежуточных по дню — из аудита <c>competition.attach-splits</c>.
/// Две формы DetailsJson: автозапуск (<c>Trigger = "import"</c>, <c>Kind</c>, счётчики дня) и
/// кнопка (сериализованный <c>SplitAttachReport</c>; пишется только у записавшего прогона).
/// Отдельной колонки «итог доклейки» у дня нет — аудит уже хранит, кто, когда и что вышло.
/// </summary>
public class SplitAttachJournal(SwimmDbContext db) : ISplitAttachJournal
{
    public async Task<IReadOnlyDictionary<int, SplitAttachNote>> GetLatestAsync(
        IReadOnlyCollection<int> competitionIds, CancellationToken ct = default)
    {
        if (competitionIds.Count == 0) return new Dictionary<int, SplitAttachNote>();
        return await LatestAsync(competitionIds.Select(id => id.ToString()).Distinct().ToList(), ct);
    }

    /// <summary>
    /// Итог последней доклейки у ВСЕХ дней, у которых она была, — фильтр «С ошибками»
    /// /Admin/Competitions смотрит все сезоны. Без списка id: длинный IN по тысячам дней не нужен,
    /// строк этого аудита — по одной-две на затянутый день.
    /// </summary>
    public Task<Dictionary<int, SplitAttachNote>> GetLatestForAllAsync(CancellationToken ct = default) =>
        LatestAsync(null, ct);

    /// <summary>
    /// Только последняя строка аудита на день (наибольший Id: аудит пишется по ходу дела, Id растёт
    /// вместе с CreatedAt), и только у неё читается DetailsJson — вся история доклеек с JSON не тянется.
    /// </summary>
    private async Task<Dictionary<int, SplitAttachNote>> LatestAsync(List<string>? keys, CancellationToken ct)
    {
        var audits = db.AdminAudits.AsNoTracking()
            .Where(a => a.Action == SplitAutoAttachRules.AuditAction && a.EntityType == "Competition" && a.EntityId != null);
        if (keys != null) audits = audits.Where(a => keys.Contains(a.EntityId!));
        var latestIds = audits.GroupBy(a => a.EntityId).Select(g => g.Max(a => a.Id));

        var rows = await db.AdminAudits.AsNoTracking()
            .Where(a => latestIds.Contains(a.Id))
            .Select(a => new { a.EntityId, a.CreatedAt, a.ActorName, a.Summary, a.DetailsJson })
            .ToListAsync(ct);

        var notes = new Dictionary<int, SplitAttachNote>();
        foreach (var r in rows)
        {
            if (!int.TryParse(r.EntityId, out var id)) continue;
            notes[id] = Parse(r.CreatedAt, r.ActorName, r.Summary, r.DetailsJson);
        }
        return notes;
    }

    internal static SplitAttachNote Parse(DateTime at, string? actor, string summary, string? json)
    {
        int swims = 0, legs = 0;
        string? kind = null, trigger = null, message = null;
        if (!string.IsNullOrEmpty(json))
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Object)
                {
                    swims = Int(root, "SwimWrites");
                    legs = Int(root, "LegWrites");
                    kind = Str(root, "Kind");
                    trigger = Str(root, "Trigger");
                    message = Str(root, "Message");
                }
            }
            catch (JsonException)
            {
                // Битый JSON — остаётся сводка строки.
            }
        }

        var auto = trigger == SplitAutoAttachRules.Trigger || actor == SplitAutoAttachRules.ActorName;
        var parsed = kind != null ? SplitAutoAttachRules.ParseCode(kind)
            : swims + legs > 0 ? SplitAutoKind.Attached : SplitAutoKind.NothingNew;
        return new SplitAttachNote(at, auto, parsed, swims, legs, message ?? summary);
    }

    private static int Int(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : 0;

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
