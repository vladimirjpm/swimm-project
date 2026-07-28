using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Repositories;

/// <summary>
/// Админский CRUD правил начисления очков (см. <see cref="IPointRulesAdminRepository"/>).
/// Пишет через owner-контекст.
///
/// Два вида правил (клубные / пловца) — разные сущности с общей частью полей, поэтому
/// маппинг разведён по <c>kind</c>, а всё остальное (валидация, шкала, кэш, гарды) общее.
///
/// Правки правил меняют очки в публичных выдачах результатов и клубного зачёта, поэтому
/// после каждой мутации сбрасывается весь кэш: точечных ключей мало не будет —
/// очки денормализованы и в <c>club-points:rules</c>, и в HTTP-ответах.
/// </summary>
public class PointRulesAdminRepository(SwimmDbContext db, ICacheService cache) : IPointRulesAdminRepository
{
    private static readonly string[] Scopes = ["all", "masters", "non-masters"];
    private static readonly string[] PointsSources = ["placement", "fina"];
    private static readonly string[] GroupBys = ["age", "age-group", "none"];

    public async Task<IReadOnlyList<PointRuleRowDto>> GetAllAsync(PointRuleKind kind)
    {
        if (kind == PointRuleKind.Clubs)
        {
            var usage = await db.Competitions.AsNoTracking()
                .Where(c => c.PointRuleClubsId != null)
                .GroupBy(c => c.PointRuleClubsId!.Value)
                .Select(g => new { RuleId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.RuleId, x => x.Count);

            var rules = await db.PointRulesClubs.AsNoTracking()
                .Select(r => new { r.Id, r.Version, r.Scope, r.EffectiveFrom, r.Description, r.ManualOnly, EntryCount = r.Entries.Count })
                .ToListAsync();

            return rules
                .Select(r => new PointRuleRowDto
                {
                    Id = r.Id,
                    Version = r.Version,
                    Scope = r.Scope,
                    EffectiveFrom = r.EffectiveFrom,
                    Description = r.Description,
                    ManualOnly = r.ManualOnly,
                    EntryCount = r.EntryCount,
                    CompetitionCount = usage.GetValueOrDefault(r.Id)
                })
                .OrderByDescending(r => r.EffectiveFrom).ThenBy(r => r.Version)
                .ToList();
        }

        var usageS = await db.Competitions.AsNoTracking()
            .Where(c => c.PointRuleSwimmersId != null)
            .GroupBy(c => c.PointRuleSwimmersId!.Value)
            .Select(g => new { RuleId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.RuleId, x => x.Count);

        var rulesS = await db.PointRulesSwimmers.AsNoTracking()
            .Select(r => new { r.Id, r.Version, r.Scope, r.EffectiveFrom, r.Description, r.ManualOnly, EntryCount = r.Entries.Count })
            .ToListAsync();

        return rulesS
            .Select(r => new PointRuleRowDto
            {
                Id = r.Id,
                Version = r.Version,
                Scope = r.Scope,
                EffectiveFrom = r.EffectiveFrom,
                Description = r.Description,
                ManualOnly = r.ManualOnly,
                EntryCount = r.EntryCount,
                CompetitionCount = usageS.GetValueOrDefault(r.Id)
            })
            .OrderByDescending(r => r.EffectiveFrom).ThenBy(r => r.Version)
            .ToList();
    }

    public async Task<PointRuleEditDto?> GetByIdAsync(PointRuleKind kind, int id)
    {
        if (kind == PointRuleKind.Clubs)
        {
            var r = await db.PointRulesClubs.AsNoTracking()
                .Include(x => x.Entries)
                .FirstOrDefaultAsync(x => x.Id == id);
            if (r == null) return null;

            return new PointRuleEditDto
            {
                Id = r.Id,
                Version = r.Version,
                EffectiveFrom = r.EffectiveFrom,
                Description = r.Description,
                Scope = r.Scope,
                DefaultPoints = r.DefaultPoints,
                MaxScoringPlace = r.MaxScoringPlace,
                ManualOnly = r.ManualOnly,
                RelayMultiplier = r.RelayMultiplier,
                Entries = MapEntries(r.Entries.Select(e => (e.Place, e.Points))),
                CompetitionCount = await db.Competitions.CountAsync(c => c.PointRuleClubsId == id)
            };
        }

        var s = await db.PointRulesSwimmers.AsNoTracking()
            .Include(x => x.Entries)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (s == null) return null;

        return new PointRuleEditDto
        {
            Id = s.Id,
            Version = s.Version,
            EffectiveFrom = s.EffectiveFrom,
            Description = s.Description,
            Scope = s.Scope,
            DefaultPoints = s.DefaultPoints,
            MaxScoringPlace = s.MaxScoringPlace,
            ManualOnly = s.ManualOnly,
            PointsSource = s.PointsSource,
            CountBestSwims = s.CountBestSwims,
            GroupBy = s.GroupBy,
            SplitByGender = s.SplitByGender,
            IncludeRelays = s.IncludeRelays,
            MinSwims = s.MinSwims,
            RecordPoints = s.RecordPoints,
            RecordTiePoints = s.RecordTiePoints,
            FinalsOnly = s.FinalsOnly,
            Entries = MapEntries(s.Entries.Select(e => (e.Place, e.Points))),
            CompetitionCount = await db.Competitions.CountAsync(c => c.PointRuleSwimmersId == id)
        };
    }

    public async Task<PointRuleSaveResult> CreateAsync(PointRuleKind kind, PointRuleInputDto input)
    {
        var error = await ValidateAsync(kind, input, excludeId: null);
        if (error != null) return PointRuleSaveResult.Fail(error);

        int id;
        if (kind == PointRuleKind.Clubs)
        {
            var rule = new PointRuleClubs();
            ApplyClubs(rule, input);
            rule.Entries = input.Entries.Select(e => new PointRuleClubsEntry { Place = e.Place, Points = e.Points }).ToList();
            db.PointRulesClubs.Add(rule);
            if (await SaveAsync() is { } fail) return fail;
            id = rule.Id;
        }
        else
        {
            var rule = new PointRuleSwimmers();
            ApplySwimmers(rule, input);
            rule.Entries = input.Entries.Select(e => new PointRuleSwimmersEntry { Place = e.Place, Points = e.Points }).ToList();
            db.PointRulesSwimmers.Add(rule);
            if (await SaveAsync() is { } fail) return fail;
            id = rule.Id;
        }

        await cache.InvalidateAllAsync();
        return PointRuleSaveResult.Ok(id);
    }

    public async Task<PointRuleSaveResult> UpdateAsync(PointRuleKind kind, int id, PointRuleInputDto input)
    {
        var error = await ValidateAsync(kind, input, excludeId: id);
        if (error != null) return PointRuleSaveResult.Fail(error);

        if (kind == PointRuleKind.Clubs)
        {
            var rule = await db.PointRulesClubs.Include(x => x.Entries).FirstOrDefaultAsync(x => x.Id == id);
            if (rule == null) return PointRuleSaveResult.Fail($"Правило #{id} не найдено");

            ApplyClubs(rule, input);
            // Шкала перезаписывается целиком. Удаление и вставку разносим по двум SaveChanges:
            // уникальный индекс (RuleId, Place) не отложенный, и «удалить 24 строки + вставить
            // новые 24» одной пачкой может упасть на пересечении мест.
            db.PointRulesClubsEntries.RemoveRange(rule.Entries);
            if (await SaveAsync() is { } fail1) return fail1;

            db.PointRulesClubsEntries.AddRange(
                input.Entries.Select(e => new PointRuleClubsEntry { RuleId = id, Place = e.Place, Points = e.Points }));
            if (await SaveAsync() is { } fail2) return fail2;
        }
        else
        {
            var rule = await db.PointRulesSwimmers.Include(x => x.Entries).FirstOrDefaultAsync(x => x.Id == id);
            if (rule == null) return PointRuleSaveResult.Fail($"Правило #{id} не найдено");

            ApplySwimmers(rule, input);
            db.PointRulesSwimmersEntries.RemoveRange(rule.Entries);
            if (await SaveAsync() is { } fail1) return fail1;

            db.PointRulesSwimmersEntries.AddRange(
                input.Entries.Select(e => new PointRuleSwimmersEntry { RuleId = id, Place = e.Place, Points = e.Points }));
            if (await SaveAsync() is { } fail2) return fail2;
        }

        await cache.InvalidateAllAsync();
        return PointRuleSaveResult.Ok(id);
    }

    public async Task<PointRuleSaveResult> DeleteAsync(PointRuleKind kind, int id)
    {
        if (kind == PointRuleKind.Clubs)
        {
            var rule = await db.PointRulesClubs.FirstOrDefaultAsync(x => x.Id == id);
            if (rule == null) return PointRuleSaveResult.Fail($"Правило #{id} не найдено");

            var used = await db.Competitions.CountAsync(c => c.PointRuleClubsId == id);
            if (used > 0) return UsedError(used);

            db.PointRulesClubs.Remove(rule);
        }
        else
        {
            var rule = await db.PointRulesSwimmers.FirstOrDefaultAsync(x => x.Id == id);
            if (rule == null) return PointRuleSaveResult.Fail($"Правило #{id} не найдено");

            var used = await db.Competitions.CountAsync(c => c.PointRuleSwimmersId == id);
            if (used > 0) return UsedError(used);

            db.PointRulesSwimmers.Remove(rule);
        }

        if (await SaveAsync() is { } fail) return fail;
        await cache.InvalidateAllAsync();
        return PointRuleSaveResult.Ok(id);
    }

    // ── helpers ────────────────────────────────────────────────────────────────

    private static PointRuleSaveResult UsedError(int used) => PointRuleSaveResult.Fail(
        $"На правило ссылаются соревнования: {used}. Сначала снимите привязку в /Admin/Competitions.");

    private static List<PointRuleEntryDto> MapEntries(IEnumerable<(int Place, int Points)> entries) =>
        entries.OrderBy(e => e.Place)
            .Select(e => new PointRuleEntryDto { Place = e.Place, Points = e.Points })
            .ToList();

    private static void ApplyClubs(PointRuleClubs rule, PointRuleInputDto input)
    {
        rule.Version = input.Version.Trim();
        rule.EffectiveFrom = input.EffectiveFrom;
        rule.Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim();
        rule.Scope = input.Scope;
        rule.DefaultPoints = input.DefaultPoints;
        rule.MaxScoringPlace = input.MaxScoringPlace;
        rule.ManualOnly = input.ManualOnly;
        rule.RelayMultiplier = input.RelayMultiplier;
    }

    private static void ApplySwimmers(PointRuleSwimmers rule, PointRuleInputDto input)
    {
        rule.Version = input.Version.Trim();
        rule.EffectiveFrom = input.EffectiveFrom;
        rule.Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim();
        rule.Scope = input.Scope;
        rule.DefaultPoints = input.DefaultPoints;
        rule.MaxScoringPlace = input.MaxScoringPlace;
        rule.ManualOnly = input.ManualOnly;
        rule.PointsSource = input.PointsSource;
        rule.CountBestSwims = input.CountBestSwims;
        rule.GroupBy = input.GroupBy;
        rule.SplitByGender = input.SplitByGender;
        rule.IncludeRelays = input.IncludeRelays;
        rule.MinSwims = input.MinSwims;
        rule.RecordPoints = input.RecordPoints;
        rule.RecordTiePoints = input.RecordTiePoints;
        rule.FinalsOnly = input.FinalsOnly;
    }

    private async Task<PointRuleSaveResult?> SaveAsync()
    {
        try
        {
            await db.SaveChangesAsync();
            return null;
        }
        catch (DbUpdateException)
        {
            return PointRuleSaveResult.Fail("Не удалось сохранить: проверьте версию (должна быть уникальной) и шкалу.");
        }
    }

    /// <summary>Валидация, общая для создания и правки. Ошибка — строкой для формы.</summary>
    public static string? Validate(PointRuleInputDto input, PointRuleKind kind)
    {
        if (string.IsNullOrWhiteSpace(input.Version)) return "Версия обязательна";
        if (!Scopes.Contains(input.Scope)) return $"Недопустимый scope «{input.Scope}»";

        if (input.MaxScoringPlace is <= 0) return "«Максимальное место» должно быть больше нуля";

        if (kind == PointRuleKind.Clubs)
        {
            if (input.RelayMultiplier < 1) return "Множитель эстафеты должен быть не меньше 1";
        }
        else
        {
            if (!PointsSources.Contains(input.PointsSource)) return $"Недопустимый источник очков «{input.PointsSource}»";
            if (!GroupBys.Contains(input.GroupBy)) return $"Недопустимая группировка «{input.GroupBy}»";
            if (input.CountBestSwims is <= 0) return "«Лучших заплывов» должно быть больше нуля";
            if (input.MinSwims is <= 0) return "«Минимум заплывов» должно быть больше нуля";
            if (input.RecordPoints is < 0 || input.RecordTiePoints is < 0) return "Очки за рекорд не могут быть отрицательными";
        }

        var places = input.Entries.Select(e => e.Place).ToList();
        if (places.Any(p => p <= 0)) return "В шкале есть место ≤ 0";
        if (places.Count != places.Distinct().Count()) return "В шкале повторяются места";

        return null;
    }

    private async Task<string?> ValidateAsync(PointRuleKind kind, PointRuleInputDto input, int? excludeId)
    {
        var error = Validate(input, kind);
        if (error != null) return error;

        var version = input.Version.Trim();
        var dup = kind == PointRuleKind.Clubs
            ? await db.PointRulesClubs.AnyAsync(r => r.Id != excludeId && r.Version == version)
            : await db.PointRulesSwimmers.AnyAsync(r => r.Id != excludeId && r.Version == version);
        if (dup) return $"Версия «{version}» уже занята другим правилом";

        return null;
    }
}
