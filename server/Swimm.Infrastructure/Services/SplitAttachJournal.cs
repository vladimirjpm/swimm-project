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
        var keys = competitionIds.Select(id => id.ToString()).Distinct().ToList();

        var rows = await db.AdminAudits.AsNoTracking()
            .Where(a => a.Action == SplitAutoAttachRules.AuditAction && a.EntityType == "Competition"
                        && a.EntityId != null && keys.Contains(a.EntityId))
            .Select(a => new { a.Id, a.EntityId, a.CreatedAt, a.ActorName, a.Summary, a.DetailsJson })
            .ToListAsync(ct);

        var notes = new Dictionary<int, SplitAttachNote>();
        foreach (var r in rows.GroupBy(r => r.EntityId!).Select(g => g.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).First()))
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
