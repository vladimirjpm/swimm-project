using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;

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
///
/// Одного кэша мало: клубный зачёт МАТЕРИАЛИЗОВАН в <c>ClubCompetitionStandings</c>, поэтому
/// правка шкалы и перепривязка соревнований запускают его пересчёт
/// (docs/points-rules-per-competition-plan.md §10.5).
/// </summary>
/// <param name="recalc">
/// Пересчёт материализованных величин. Необязателен: null — пересчёт пропускается (тесты).
/// </param>
public class PointRulesAdminRepository(
    SwimmDbContext db,
    ICacheService cache,
    ICompetitionRecalculationService? recalc = null) : IPointRulesAdminRepository
{
    private static readonly string[] Scopes = ["all", "masters", "non-masters"];
    private static readonly string[] PointsSources = ["placement", "fina"];
    private static readonly string[] GroupBys = ["age", "age-group", "none"];

    public async Task<IReadOnlyList<PointRuleRowDto>> GetAllAsync(PointRuleKind kind)
    {
        if (kind == PointRuleKind.Clubs)
        {
            // Считаем логические соревнования, а не строки-дни: у многодневного события
            // правило записано в каждый день, но в списке это одно соревнование — иначе
            // счётчик расходится с панелью «Соревнования».
            var usageRows = await db.Competitions.AsNoTracking()
                .Where(c => c.PointRuleClubsId != null)
                .Select(c => new
                {
                    RuleId = c.PointRuleClubsId!.Value,
                    LogicalId = c.EventId ?? -c.Id,
                    VerifiedKind = c.ClubPointsVerifiedKind
                })
                .ToListAsync();

            var usage = usageRows
                .GroupBy(x => x.RuleId)
                .ToDictionary(g => g.Key, g => g.Select(x => x.LogicalId).Distinct().Count());

            // Отмечено — если отмечен хотя бы один день события (отметка ставится всем дням сразу).
            var verified = CountByKind(usageRows.Select(x => (x.RuleId, x.LogicalId, x.VerifiedKind)),
                PointsVerifiedKinds.Official);
            var accepted = CountByKind(usageRows.Select(x => (x.RuleId, x.LogicalId, x.VerifiedKind)),
                PointsVerifiedKinds.Accepted);
            var mismatch = CountByKind(usageRows.Select(x => (x.RuleId, x.LogicalId, x.VerifiedKind)),
                PointsVerifiedKinds.Mismatch);

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
                    CompetitionCount = usage.GetValueOrDefault(r.Id),
                    VerifiedCount = verified.GetValueOrDefault(r.Id),
                    AcceptedCount = accepted.GetValueOrDefault(r.Id),
                    MismatchCount = mismatch.GetValueOrDefault(r.Id)
                })
                .OrderByDescending(r => r.EffectiveFrom).ThenBy(r => r.Version)
                .ToList();
        }

        var usageRowsS = await db.Competitions.AsNoTracking()
            .Where(c => c.PointRuleSwimmersId != null)
            .Select(c => new
            {
                RuleId = c.PointRuleSwimmersId!.Value,
                LogicalId = c.EventId ?? -c.Id,
                VerifiedKind = c.SwimmersPointsVerifiedKind
            })
            .ToListAsync();

        var usageS = usageRowsS
            .GroupBy(x => x.RuleId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.LogicalId).Distinct().Count());

        var verifiedS = CountByKind(usageRowsS.Select(x => (x.RuleId, x.LogicalId, x.VerifiedKind)),
            PointsVerifiedKinds.Official);
        var acceptedS = CountByKind(usageRowsS.Select(x => (x.RuleId, x.LogicalId, x.VerifiedKind)),
            PointsVerifiedKinds.Accepted);
        var mismatchS = CountByKind(usageRowsS.Select(x => (x.RuleId, x.LogicalId, x.VerifiedKind)),
            PointsVerifiedKinds.Mismatch);

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
                CompetitionCount = usageS.GetValueOrDefault(r.Id),
                VerifiedCount = verifiedS.GetValueOrDefault(r.Id),
                AcceptedCount = acceptedS.GetValueOrDefault(r.Id),
                MismatchCount = mismatchS.GetValueOrDefault(r.Id)
            })
            .OrderByDescending(r => r.EffectiveFrom).ThenBy(r => r.Version)
            .ToList();
    }

    /// <summary>Сколько логических соревнований правила помечено данным итогом проверки.</summary>
    private static Dictionary<int, int> CountByKind(
        IEnumerable<(int RuleId, int LogicalId, string? VerifiedKind)> rows, string wanted) =>
        rows.GroupBy(x => x.RuleId)
            .ToDictionary(
                g => g.Key,
                g => g.GroupBy(x => x.LogicalId).Count(day => day.Any(x => x.VerifiedKind == wanted)));

    // ── Панель «Соревнования правила» (перепривязка на месте) ──────────────────

    public async Task<IReadOnlyList<PointRuleCompetitionRowDto>> GetCompetitionsAsync(
        PointRuleKind kind, int ruleId)
    {
        var q = db.Competitions.AsNoTracking();
        q = kind == PointRuleKind.Clubs
            ? q.Where(c => c.PointRuleClubsId == ruleId)
            : q.Where(c => c.PointRuleSwimmersId == ruleId);

        var days = await q
            .Select(c => new
            {
                c.Id,
                c.EventId,
                c.Name,
                EventName = c.Event != null ? c.Event.Name : null,
                c.Date,
                c.IsMasters,
                c.OrgCompId,
                VerifiedAt = kind == PointRuleKind.Clubs ? c.ClubPointsVerifiedAt : c.SwimmersPointsVerifiedAt,
                VerifiedBy = kind == PointRuleKind.Clubs ? c.ClubPointsVerifiedBy : c.SwimmersPointsVerifiedBy,
                VerifiedKind = kind == PointRuleKind.Clubs ? c.ClubPointsVerifiedKind : c.SwimmersPointsVerifiedKind
            })
            .ToListAsync();

        if (days.Count == 0) return [];

        var dayIds = days.Select(d => d.Id).ToList();
        var resultCounts = await db.Results.AsNoTracking()
            .Where(r => dayIds.Contains(r.CompetitionId))
            .GroupBy(r => r.CompetitionId)
            .Select(g => new { CompetitionId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CompetitionId, x => x.Count);

        // Дата хранится строкой dd/MM/yyyy — сортируем по разобранной дате, а не лексикографически.
        static DateOnly? ParseDate(string? raw) =>
            DateOnly.TryParseExact(raw, "dd/MM/yyyy", out var d) ? d : null;

        return days
            .GroupBy(d => d.EventId ?? -d.Id)
            .Select(g =>
            {
                var head = g.OrderBy(d => ParseDate(d.Date) ?? DateOnly.MaxValue).ThenBy(d => d.Id).First();
                return new PointRuleCompetitionRowDto
                {
                    Id = head.Id,
                    EventId = head.EventId,
                    Name = head.EventId != null ? head.EventName ?? head.Name : head.Name,
                    Date = head.Date,
                    DayCount = g.Count(),
                    ResultCount = g.Sum(d => resultCounts.GetValueOrDefault(d.Id)),
                    IsMasters = head.IsMasters,
                    // У многодневки OrgCompId проставлен не каждому дню — берём первый непустой.
                    OrgCompId = g.Select(d => d.OrgCompId).FirstOrDefault(x => x != null),
                    // Отметка ставится всем дням сразу; берём первую заполненную — на случай,
                    // если дни добавили уже после сверки.
                    VerifiedAt = g.Select(d => d.VerifiedAt).FirstOrDefault(x => x != null),
                    VerifiedBy = g.Select(d => d.VerifiedBy).FirstOrDefault(x => x != null),
                    VerifiedKind = g.Select(d => d.VerifiedKind).FirstOrDefault(x => x != null),
                    RuleId = ruleId
                };
            })
            .OrderByDescending(r => ParseDate(r.Date) ?? DateOnly.MinValue)
            .ThenBy(r => r.Name)
            .ToList();
    }

    public async Task<PointRuleSaveResult> ReassignCompetitionsAsync(
        PointRuleKind kind, IReadOnlyList<PointRuleReassignItem> items)
    {
        if (items.Count == 0) return PointRuleSaveResult.Ok(0);

        // Целевые правила должны существовать — иначе FK оборвёт сохранение без объяснений.
        var targetIds = items.Where(i => i.RuleId != null).Select(i => i.RuleId!.Value).Distinct().ToList();
        if (targetIds.Count > 0)
        {
            var known = kind == PointRuleKind.Clubs
                ? await db.PointRulesClubs.Where(r => targetIds.Contains(r.Id)).Select(r => r.Id).ToListAsync()
                : await db.PointRulesSwimmers.Where(r => targetIds.Contains(r.Id)).Select(r => r.Id).ToListAsync();

            var missing = targetIds.Except(known).ToList();
            if (missing.Count > 0)
                return PointRuleSaveResult.Fail($"Правило #{missing[0]} не найдено");
        }

        var headIds = items.Select(i => i.CompetitionId).Distinct().ToList();
        var heads = await db.Competitions.AsNoTracking()
            .Where(c => headIds.Contains(c.Id))
            .Select(c => new { c.Id, c.EventId })
            .ToListAsync();

        var changed = 0;
        var changedHeads = new List<int>();
        foreach (var item in items)
        {
            var head = heads.FirstOrDefault(h => h.Id == item.CompetitionId);
            if (head == null) continue;

            // Регламент у многодневного события один → правило проставляется всем его дням.
            var dayIds = head.EventId is int ev
                ? await db.Competitions.Where(c => c.EventId == ev).Select(c => c.Id).ToListAsync()
                : [head.Id];

            var days = await db.Competitions.Where(c => dayIds.Contains(c.Id)).ToListAsync();
            var touched = false;

            foreach (var day in days)
            {
                if (kind == PointRuleKind.Clubs)
                {
                    if (day.PointRuleClubsId == item.RuleId) continue;
                    day.PointRuleClubsId = item.RuleId;
                }
                else
                {
                    if (day.PointRuleSwimmersId == item.RuleId) continue;
                    day.PointRuleSwimmersId = item.RuleId;
                }
                touched = true;
            }

            if (touched)
            {
                changed++;
                changedHeads.Add(head.Id);
            }
        }

        if (changed == 0) return PointRuleSaveResult.Ok(0);

        await db.SaveChangesAsync();
        await cache.InvalidateAllAsync();

        // Перепривязка отсюда — тот же случай, что и в /Admin/Competitions: клубный зачёт
        // материализован, без пересчёта соревнование осталось бы на очках прежнего правила.
        // Зачётная единица — событие целиком, поэтому хватает «головы» из items.
        if (kind == PointRuleKind.Clubs)
            await RebuildStandingsAsync(changedHeads);

        return PointRuleSaveResult.Ok(changed);
    }

    public async Task<PointRuleSaveResult> ToggleVerifiedAsync(
        PointRuleKind kind, int competitionId, string verifiedKind, string? user)
    {
        if (!PointsVerifiedKinds.IsKnown(verifiedKind))
            return PointRuleSaveResult.Fail($"Неизвестный итог проверки «{verifiedKind}»");

        var head = await db.Competitions.AsNoTracking()
            .Where(c => c.Id == competitionId)
            .Select(c => new { c.Id, c.EventId })
            .FirstOrDefaultAsync();
        if (head == null) return PointRuleSaveResult.Fail($"Соревнование #{competitionId} не найдено");

        // Сверяют соревнование целиком, а не отдельный день — отметка идёт всем дням события.
        var days = head.EventId is int ev
            ? await db.Competitions.Where(c => c.EventId == ev).ToListAsync()
            : await db.Competitions.Where(c => c.Id == head.Id).ToListAsync();

        // Состояния взаимоисключающие: повторный клик по текущему снимает отметку,
        // клик по другому — переключает на него (а не добавляет второе).
        var current = kind == PointRuleKind.Clubs
            ? days.Select(d => d.ClubPointsVerifiedKind).FirstOrDefault(k => k != null)
            : days.Select(d => d.SwimmersPointsVerifiedKind).FirstOrDefault(k => k != null);

        var clearing = current == verifiedKind;

        var now = clearing ? (DateTime?)null : DateTime.UtcNow;
        var by = clearing ? null : user;
        var mark = clearing ? null : verifiedKind;

        foreach (var day in days)
        {
            if (kind == PointRuleKind.Clubs)
            {
                day.ClubPointsVerifiedAt = now;
                day.ClubPointsVerifiedBy = by;
                day.ClubPointsVerifiedKind = mark;
            }
            else
            {
                day.SwimmersPointsVerifiedAt = now;
                day.SwimmersPointsVerifiedBy = by;
                day.SwimmersPointsVerifiedKind = mark;
            }
        }

        await db.SaveChangesAsync();
        // Кэш не трогаем: отметка админская, в публичные выдачи не попадает.
        return PointRuleSaveResult.Ok(now == null ? 0 : 1);
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

        // Привязок у нового правила ещё нет, но НЕ-ManualOnly правило сразу входит в
        // автоподбор и может перехватить соревнования без FK — их зачёт станет неверным.
        if (kind == PointRuleKind.Clubs && !input.ManualOnly)
            await RebuildStandingsAsync(await UnitsScoredByRuleAsync(id));

        return PointRuleSaveResult.Ok(id);
    }

    public async Task<PointRuleSaveResult> UpdateAsync(PointRuleKind kind, int id, PointRuleInputDto input)
    {
        var error = await ValidateAsync(kind, input, excludeId: id);
        if (error != null) return PointRuleSaveResult.Fail(error);

        // Кого правило считало ДО правки. Нужно именно «до»: правка EffectiveFrom / Scope /
        // ManualOnly сдвигает автоподбор, и соревнование уедет на другое правило — его очки
        // тоже станут неверными, а после сохранения оно в выборку уже не попадёт.
        var unitsBefore = kind == PointRuleKind.Clubs ? await UnitsScoredByRuleAsync(id) : [];
        var scoringChanged = false;

        if (kind == PointRuleKind.Clubs)
        {
            var rule = await db.PointRulesClubs.Include(x => x.Entries).FirstOrDefaultAsync(x => x.Id == id);
            if (rule == null) return PointRuleSaveResult.Fail($"Правило #{id} не найдено");

            // Переименование или новое описание зачёт не меняют — гонять по ним пересчёт
            // соревнований незачем.
            scoringChanged = ShapeOf(rule) != ShapeOf(input);

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

        // Клубный зачёт материализован — правка шкалы обязана его пересчитать. Очки пловцов
        // (High Point) считаются на лету (Э6), им хватает сброса кэша.
        if (scoringChanged)
            await RebuildStandingsAsync(unitsBefore.Concat(await UnitsScoredByRuleAsync(id)));

        return PointRuleSaveResult.Ok(id);
    }

    public async Task<PointRuleSaveResult> DeleteAsync(PointRuleKind kind, int id)
    {
        // Соревнования с явной привязкой удалить не дадут (гард ниже), но автоподбор на
        // удаляемое правило снять некому — эти зачёты осиротеют на старых очках.
        var unitsBefore = kind == PointRuleKind.Clubs ? await UnitsScoredByRuleAsync(id) : [];

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
        await RebuildStandingsAsync(unitsBefore);
        return PointRuleSaveResult.Ok(id);
    }

    // ── пересчёт материализованного клубного зачёта ────────────────────────────

    /// <summary>
    /// Всё, из чего клубный зачёт считает очки. Версия и описание сюда не входят намеренно:
    /// переименование правила зачёт не меняет.
    /// </summary>
    private sealed record ClubScoringShape(
        string Scope, DateOnly EffectiveFrom, bool ManualOnly,
        int DefaultPoints, int? MaxScoringPlace, int RelayMultiplier, string Scale);

    private static ClubScoringShape ShapeOf(PointRuleClubs r) => new(
        r.Scope, r.EffectiveFrom, r.ManualOnly, r.DefaultPoints, r.MaxScoringPlace, r.RelayMultiplier,
        ScaleKey(r.Entries.Select(e => (e.Place, e.Points))));

    private static ClubScoringShape ShapeOf(PointRuleInputDto i) => new(
        i.Scope, i.EffectiveFrom, i.ManualOnly, i.DefaultPoints, i.MaxScoringPlace, i.RelayMultiplier,
        ScaleKey(i.Entries.Select(e => (e.Place, e.Points))));

    private static string ScaleKey(IEnumerable<(int Place, int Points)> entries) =>
        string.Join(",", entries.OrderBy(e => e.Place).Select(e => $"{e.Place}:{e.Points}"));

    /// <summary>
    /// Зачётные единицы (событие целиком либо однодневка), которые СЕЙЧАС считаются по этому
    /// правилу — и по явной привязке, и через автоподбор: у соревнований без FK правка
    /// правила-по-умолчанию меняет очки ровно так же.
    ///
    /// Берём только соревнования с материализованным зачётом: у остальных устаревать нечему,
    /// а прогон по всем 600+ соревнованиям правила 1 повесил бы админский POST.
    /// </summary>
    private async Task<List<int>> UnitsScoredByRuleAsync(int ruleId)
    {
        if (recalc is null) return [];

        var withStandings = await db.ClubCompetitionStandings.AsNoTracking()
            .Select(s => s.CompetitionId).Distinct().ToListAsync();
        if (withStandings.Count == 0) return [];

        // Шкала (Entries) для выбора правила не нужна — только Scope/EffectiveFrom/ManualOnly.
        var rules = await db.PointRulesClubs.AsNoTracking().ToListAsync();
        var comps = await db.Competitions.AsNoTracking()
            .Where(c => withStandings.Contains(c.Id))
            .Select(c => new { c.Id, c.EventId, c.IsMasters, c.Date, c.PointRuleClubsId })
            .ToListAsync();

        return comps
            .Where(c => CompetitionRuleResolver
                .Resolve(rules, c.PointRuleClubsId, c.IsMasters, ParseDate(c.Date))?.Id == ruleId)
            .GroupBy(c => c.EventId ?? -c.Id)
            .Select(g => g.OrderBy(c => c.Id).First().Id)
            .ToList();
    }

    private async Task RebuildStandingsAsync(IEnumerable<int> unitHeadIds)
    {
        if (recalc is null) return;

        foreach (var id in unitHeadIds.Distinct())
        {
            // Правило уже сохранено — сбой пересчёта не должен отменять правку формы.
            // Аварийный прогон: `dotnet run -- --rebuild-club-standings`.
            try { await recalc.RecalculateCompetitionAsync(id); }
            catch (Exception) { /* лог не нужен: аварийный прогон закрывает случай */ }
        }
    }

    /// <summary>Дата соревнования — строка dd/MM/yyyy; без неё правило подбирается на сегодня.</summary>
    private static DateOnly ParseDate(string? date) =>
        DateOnly.TryParseExact(date, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d
            : DateOnly.FromDateTime(DateTime.UtcNow);

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
