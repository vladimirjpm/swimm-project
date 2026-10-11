using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Swimm.Application.Abstractions;
using Swimm.Application.Mapping;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Реализация <see cref="IEnglishNameRepairService"/>. Каждое решение — в отчёт и в аудит
/// (<c>swimmer.en-name-fix</c>: было / стало / карточка / причина в <c>DetailsJson</c>), так что
/// откат любой строки — по аудиту, без бэкапа.
/// </summary>
public sealed class EnglishNameRepairService(
    SwimmDbContext db, ILogligClient loglig, ILogger<EnglishNameRepairService> logger) : IEnglishNameRepairService
{
    /// <summary>Пауза между карточками: это чужой прод, а кандидатов десятки.</summary>
    internal static TimeSpan PoliteDelay { get; set; } = TimeSpan.FromSeconds(1);

    internal const string AuditAction = "swimmer.en-name-fix";

    /// <summary>Иврит в аудите — как есть, а не «א…»: «было / стало» читают глазами.</summary>
    private static readonly JsonSerializerOptions AuditJson = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public async Task<EnglishNameRepairReport> RepairAsync(
        bool apply, IReadOnlyCollection<int>? onlySwimmerIds = null, CancellationToken ct = default)
    {
        var swimmers = await db.Swimmers.AsNoTracking()
            .Where(s => s.LastNameEn != "" || s.FirstNameEn != "")
            .Where(s => onlySwimmerIds == null || onlySwimmerIds.Contains(s.Id))
            .Select(s => new { s.Id, s.LastName, s.FirstName, s.LastNameEn, s.FirstNameEn, s.BirthYear, s.LogligId })
            .ToListAsync(ct);

        var candidates = swimmers
            .Where(s => EnglishNameFit.Assess(s.LastName, s.FirstName, s.LastNameEn, s.FirstNameEn).Verdict
                is EnglishNameVerdict.HebrewInLatin or EnglishNameVerdict.Foreign)
            .OrderBy(s => s.Id)
            .ToList();

        var rows = new List<EnglishNameRepairRow>();
        var first = true;
        foreach (var s in candidates)
        {
            string? card = null;
            if (s.LogligId is int logligId)
            {
                if (!first) await Task.Delay(PoliteDelay, ct);
                first = false;
                card = await loglig.GetPlayerEnglishNameAsync(logligId, ct);
            }

            var decision = EnglishNameRepairPlanner.Decide(s.LastName, s.FirstName, s.LastNameEn, s.FirstNameEn, card);
            rows.Add(new EnglishNameRepairRow(
                s.Id, $"{s.LastName} {s.FirstName}", s.BirthYear, s.LogligId, s.LastNameEn, s.FirstNameEn, card, decision));
        }

        var skipped = 0;
        if (apply) skipped = await ApplyAsync(rows, ct);

        return new EnglishNameRepairReport(
            apply, rows,
            rows.Count(r => r.Decision.Action == EnglishNameRepairAction.Write),
            rows.Count(r => r.Decision.Action == EnglishNameRepairAction.Clear),
            rows.Count(r => r.Decision.Action == EnglishNameRepairAction.Keep),
            skipped);
    }

    /// <summary>
    /// Пишет решения Write/Clear одной транзакцией. Строка пропускается, если EN-поля пловца
    /// изменились с момента чтения (параллельный импорт, ручная правка) — чужую правку не
    /// перетираем. Возвращает число пропущенных.
    /// </summary>
    private async Task<int> ApplyAsync(IReadOnlyList<EnglishNameRepairRow> rows, CancellationToken ct)
    {
        var toChange = rows.Where(r => r.Decision.Action != EnglishNameRepairAction.Keep).ToList();
        if (toChange.Count == 0) return 0;

        var ids = toChange.Select(r => r.SwimmerId).ToList();
        var skipped = 0;
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            skipped = 0;
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var tracked = await db.Swimmers.Where(s => ids.Contains(s.Id)).ToDictionaryAsync(s => s.Id, ct);

            foreach (var r in toChange)
            {
                if (!tracked.TryGetValue(r.SwimmerId, out var s)
                    || s.LastNameEn != r.OldLastEn || s.FirstNameEn != r.OldFirstEn)
                {
                    skipped++;
                    logger.LogWarning("EN-имя #{Id}: поля изменились после чтения — пропущено", r.SwimmerId);
                    continue;
                }

                s.LastNameEn = r.Decision.LastEn;
                s.FirstNameEn = r.Decision.FirstEn;
                db.AdminAudits.Add(new AdminAudit
                {
                    ActorName = "cli --fix-en-names",
                    Action = AuditAction,
                    EntityType = "Swimmer",
                    EntityId = r.SwimmerId.ToString(),
                    Summary = Truncate(
                        $"EN-имя ({r.Decision.Action}): «{r.OldLastEn} / {r.OldFirstEn}» → "
                        + $"«{r.Decision.LastEn} / {r.Decision.FirstEn}» — {r.Decision.Reason}", 1000),
                    DetailsJson = JsonSerializer.Serialize(new
                    {
                        action = r.Decision.Action.ToString(),
                        old = new { LastNameEn = r.OldLastEn, FirstNameEn = r.OldFirstEn },
                        @new = new { LastNameEn = r.Decision.LastEn, FirstNameEn = r.Decision.FirstEn },
                        loglig = r.Card,
                        logligId = r.LogligId,
                        reason = r.Decision.Reason,
                    }, AuditJson),
                });
            }

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });
        return skipped;
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
}
