using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Constants;
using Swimm.Application.Dtos;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services.DataChecks;

/// <summary>Пловцы-сироты: ничего на них не ссылается (в т.ч. ноги эстафет — урок И-2).</summary>
public sealed class SwimmerOrphanCheck(ISwimmerDedupService dedup) : IDataCheck
{
    public string Id => "swimmers.orphans";
    public string Title => "Пловцы-сироты";
    public string Description => "Ни результатов, ни ног эстафет, ни групп, ни избранного, ни аккаунта. Удаляются кнопкой на /Admin/Swimmers.";
    public DataCheckSeverity Severity => DataCheckSeverity.Info;

    public async Task<DataCheckOutcome> RunAsync(CancellationToken ct = default)
    {
        var report = await dedup.FindCandidatesAsync(ct);
        return new DataCheckOutcome(report.Orphans.Count, report.Orphans
            .Take(50)
            .Select(o => new DataCheckItem(
                "Swimmer", o.Id, $"{o.Name} ({o.BirthYear})", o.Club, $"/Admin/Swimmers/Edit?id={o.Id}",
                PublicRoutes.Swimmer(o.Id)))
            .ToList());
    }
}

/// <summary>Уверенные дубли пловцов — ждут склейки.</summary>
public sealed class SwimmerDedupCheck(ISwimmerDedupService dedup) : IDataCheck
{
    public string Id => "swimmers.dedup-sure";
    public string Title => "Уверенные дубли пловцов";
    public string Description => "Пары с почти одинаковым именем, годом и полом. Склеиваются на /Admin/Swimmers; перед merge смотри на общий заплыв — близнецы не дубли.";
    public DataCheckSeverity Severity => DataCheckSeverity.Warning;

    public async Task<DataCheckOutcome> RunAsync(CancellationToken ct = default)
    {
        var report = await dedup.FindCandidatesAsync(ct);
        var sure = report.Candidates.Where(c => c.Sure).ToList();
        return new DataCheckOutcome(sure.Count, sure
            .Take(50)
            .Select(c => new DataCheckItem(
                "Swimmer", c.CanonicalId,
                $"{c.CanonicalName} #{c.CanonicalId} ← #{c.DuplicateId} {c.DuplicateName}",
                $"год {c.BirthYear}, расстояние имён {c.Distance}, {c.CanonicalResults} vs {c.DuplicateResults} результатов",
                "/Admin/Swimmers?filter=dedup-sure",
                // Ведём на КАНОН: перед merge смотрят его заплывы, а не дубля.
                PublicRoutes.Swimmer(c.CanonicalId),
                // Пара целиком: «Принять» на такой находке значит «тёзки, не склеивать» и
                // пишет её в тот же Sys_DedupIgnoredPairs, что кнопка ✕ на /Admin/Swimmers.
                FixKind: DataCheckFixKinds.DedupIgnore,
                FixEntityId: c.DuplicateId))
            .ToList());
    }
}

/// <summary>Клубы-дубли с одинаковым именем — след импорта (инцидент И-9).</summary>
public sealed class ClubDedupCheck(IClubDedupService dedup) : IDataCheck
{
    public string Id => "clubs.dedup-sure";
    public string Title => "Уверенные дубли клубов";
    public string Description => "Одно имя у разных Id — обычно след импорта, у канона заполнен NameEn, у дубля нет. Склеивается на /Admin/Clubs.";
    public DataCheckSeverity Severity => DataCheckSeverity.Warning;

    public async Task<DataCheckOutcome> RunAsync(CancellationToken ct = default)
    {
        var report = await dedup.FindCandidatesAsync(ct);
        var sure = report.Candidates.Where(c => c.Sure).ToList();
        return new DataCheckOutcome(sure.Count, sure
            .Take(50)
            .Select(c => new DataCheckItem(
                "Club", c.CanonicalId,
                $"{c.CanonicalName} #{c.CanonicalId} ({c.CanonicalResults}) ← #{c.DuplicateId} ({c.DuplicateResults})",
                $"эвристика {c.Heuristic}",
                "/Admin/Clubs?filter=dedup-sure",
                PublicRoutes.Club(c.CanonicalId),
                // Та же логика, что у пловцов: «Принять» = «не дубли», пара уходит в
                // Sys_DedupIgnoredPairs и больше не всплывает и на /Admin/Clubs.
                FixKind: DataCheckFixKinds.DedupIgnore,
                FixEntityId: c.DuplicateId))
            .ToList());
    }
}

/// <summary>Пустые клубы: ни пловцов, ни результатов — мусор парсера.</summary>
public sealed class EmptyClubCheck(IDataQualityService quality) : IDataCheck
{
    public string Id => "clubs.empty";
    public string Title => "Пустые клубы";
    public string Description => "Ни пловцов, ни результатов. Удаляются кнопкой «Удалить все пустые» на /Admin/Clubs.";
    public DataCheckSeverity Severity => DataCheckSeverity.Info;

    public async Task<DataCheckOutcome> RunAsync(CancellationToken ct = default)
    {
        var res = await quality.GetClubQualityAsync("no-swimmers", ct);
        return new DataCheckOutcome(res.Total, res.Items
            .Select(c => new DataCheckItem("Club", c.Id, c.Name, c.NameEn, $"/Admin/Clubs/Edit?id={c.Id}",
                PublicRoutes.Club(c.Id)))
            .ToList());
    }
}

/// <summary>
/// Соревнование с результатами, но без привязанного правила клубных очков
/// (docs/points-rules-per-competition-plan.md, §9.3).
///
/// Молчащий отказ: без FK зачёт уходит на страховочный подбор по дате и scope, а тот
/// «едет» при заведении новой версии правила — цифры прошлого сезона меняются задним
/// числом. Обнаружить это можно только по странным суммам Top Clubs, поэтому индикатор
/// и просился в реестр.
///
/// Проверяем ТОЛЬКО чемпионаты, мастерс и Маккабиаду (решение Влада 2026-08-10): клубный
/// зачёт ведут по ним. Лиги, отборочные («מוקדמות») и товарищеские старты живут без правила
/// законно — на реальных данных из 19 находок настоящими были 3, остальные звали чинить то,
/// что чинить не нужно. Пометка «клубный зачёт не ведётся» (<c>ClubPointsDisabled</c>)
/// продолжает работать поверх (решение Р19) — ею глушат исключения внутри самих чемпионатов.
///
/// Правило ПЛОВЦОВ сознательно не проверяем: «не привязано → legacy-расчёт по FINA» —
/// легитимный режим (masters и Маккабиада живут так намеренно), и находка по каждому
/// такому соревнованию была бы ложной тревогой.
/// </summary>
public sealed class CompetitionWithoutClubPointRuleCheck(SwimmDbContext db) : IDataCheck
{
    /// <summary>Ключ категории Маккабиады — она не помечена ни чемпионатом, ни мастерсом.</summary>
    private const string MaccabiahCategoryKey = "result-maccabiah";

    public string Id => "competitions.no-club-point-rule";
    public string Title => "Соревнования без правила клубных очков";
    public string Description =>
        "У чемпионата (или мастерса/Маккабиады) есть результаты, но не привязано правило клубных " +
        "очков — зачёт считается страховочным подбором по дате, и новая версия правила сдвинет " +
        "цифры задним числом. Правило выбирается прямо здесь; массово — на " +
        "/Admin/Competitions/AssignRules. Лиги и отборочные не проверяются: они без зачёта законно.";
    public DataCheckSeverity Severity => DataCheckSeverity.Warning;

    public async Task<DataCheckOutcome> RunAsync(CancellationToken ct = default)
    {
        // Пустые соревнования пропускаем: считать там нечего, и про них уже кричит
        // отдельная проверка competitions.empty — две находки на одну причину только шумят.
        var rows = await db.Competitions.AsNoTracking()
            .Where(c => c.PointRuleClubsId == null && !c.ClubPointsDisabled
                        // Маккабиада — своя категория лестницы («соревнование само по себе»),
                        // флага у неё нет, поэтому опознаётся членством в категории.
                        && (c.IsChampionship || c.IsMasters
                            || db.CategoryCompetitions.Any(cc =>
                                cc.CompetitionId == c.Id && cc.Category.Key == MaccabiahCategoryKey))
                        && db.Results.Any(r => r.CompetitionId == c.Id))
            .Select(c => new
            {
                c.Id, c.Name, c.Date, c.IsMasters,
                Rows = db.Results.Count(r => r.CompetitionId == c.Id)
            })
            .OrderByDescending(c => c.Rows)
            .ToListAsync(ct);

        if (rows.Count == 0) return DataCheckOutcome.Empty;

        return new DataCheckOutcome(rows.Count, rows
            .Take(50)
            .Select(c => new DataCheckItem(
                "Competition", c.Id,
                c.Name,
                $"{c.Date}, строк {c.Rows}" + (c.IsMasters ? ", masters" : ""),
                $"/Admin/Competitions/Edit?id={c.Id}",
                PublicRoutes.Competition(c.Id),
                // Правило выбирается прямо в находке — заходить в карточку ради одного
                // селекта незачем (см. DataCheckFixKinds.CompetitionClubRule).
                FixKind: DataCheckFixKinds.CompetitionClubRule,
                FixEntityId: c.Id))
            .ToList());
    }
}

/// <summary>
/// Сверка импорта не сошлась (фаза Д1): в БД не то число строк, что в файле-протоколе.
/// Берётся ПОСЛЕДНЯЯ сверка по каждому соревнованию — предыдущие уже неактуальны.
/// </summary>
public sealed class ReconciliationMismatchCheck(SwimmDbContext db) : IDataCheck
{
    public string Id => "import.reconciliation-mismatch";
    public string Title => "Импорт разошёлся с протоколом";
    public string Description =>
        "Последняя сверка соревнования показала другое число строк, чем в файле. " +
        "Лечится переимпортом с «удалять лишние»; если расходится дата — сперва разберись с ней (И-8).";
    public DataCheckSeverity Severity => DataCheckSeverity.Error;

    public async Task<DataCheckOutcome> RunAsync(CancellationToken ct = default)
    {
        // Последний прогон сверки по каждому соревнованию: сравниваем итоговые строки
        // (EventKey = "") — по ним видно масштаб, не складывая десятки заплывов.
        var latest = await db.ImportReconciliations.AsNoTracking()
            .Where(r => r.EventKey == "")
            .GroupBy(r => r.CompetitionId)
            .Select(g => g.OrderByDescending(x => x.ImportedAt).ThenByDescending(x => x.Id).First())
            .ToListAsync(ct);

        var bad = latest.Where(r => r.Status == "mismatch").ToList();
        if (bad.Count == 0) return DataCheckOutcome.Empty;

        var names = await db.Competitions.AsNoTracking()
            .Where(c => bad.Select(b => b.CompetitionId).Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name + " · " + c.Date, ct);

        return new DataCheckOutcome(bad.Count, bad
            .OrderBy(r => r.CompetitionId)
            .Select(r => new DataCheckItem(
                "Competition", r.CompetitionId,
                $"{names.GetValueOrDefault(r.CompetitionId, $"#{r.CompetitionId}")}: файл {r.ExpectedRows}, БД {r.ActualRows}",
                $"сверка от {r.ImportedAt:dd.MM.yyyy HH:mm}, файл {r.ImportFileName}",
                $"/Admin/Competitions?q={r.CompetitionId}",
                PublicRoutes.Competition(r.CompetitionId)))
            .ToList());
    }
}

/// <summary>
/// Ожившие надгробия: клуб склеен (<c>MergedIntoId</c>), но на нём снова висят результаты
/// или пловцы.
///
/// Инцидент И-13. Merge — мягкий: строка дубля остаётся надгробием, чтобы ссылки на старый Id
/// не гнили. Импорт склеенные исключал в фоллбеке по имени, но НЕ в точном матче по паре
/// Name|NameEn — а у надгробия NameEn обычно пустой, как и в ивритском протоколе. Переимпорт
/// попадал ровно в надгробие, и merge молча откатывался: страница канона теряла половину
/// истории (клуб #438 показывал зимний чемпионат «Young» на сезон раньше — его зачёт уехал
/// на надгробие #1291).
///
/// Своими силами страница клуба этого не покажет: она читает только канон, а данные лежат
/// на невидимом клубе. Поэтому нужен именно сторож в реестре.
/// </summary>
public sealed class MergedClubStillUsedCheck(SwimmDbContext db) : IDataCheck
{
    public string Id => "clubs.merged-still-used";
    public string Title => "Склеенные клубы с данными";
    public string Description =>
        "Клуб склеен в другой, но на нём снова есть результаты/пловцы — значит склейку что-то " +
        "откатило (обычно переимпорт). Данные надо перевесить на канон и пересчитать зачёты.";
    public DataCheckSeverity Severity => DataCheckSeverity.Error;

    public async Task<DataCheckOutcome> RunAsync(CancellationToken ct = default)
    {
        var rows = await db.Clubs.AsNoTracking()
            .Where(c => c.MergedIntoId != null)
            .Select(c => new
            {
                c.Id,
                c.Name,
                c.MergedIntoId,
                Results = db.Results.Count(r => r.ClubId == c.Id),
                Swimmers = db.Swimmers.Count(s => s.ClubId == c.Id),
                Standings = db.ClubCompetitionStandings.Count(s => s.ClubId == c.Id),
            })
            .Where(c => c.Results > 0 || c.Swimmers > 0 || c.Standings > 0)
            .OrderByDescending(c => c.Results)
            .ToListAsync(ct);

        if (rows.Count == 0) return DataCheckOutcome.Empty;

        return new DataCheckOutcome(rows.Count, rows
            .Take(50)
            .Select(c => new DataCheckItem(
                "Club", c.Id,
                $"#{c.Id} {c.Name} → канон #{c.MergedIntoId}",
                $"результатов {c.Results}, пловцов {c.Swimmers}, строк зачёта {c.Standings}",
                $"/Admin/Clubs/Edit?id={c.Id}",
                // Ведём на КАНОН: смотреть надо страницу, которая теряет данные.
                PublicRoutes.Club(c.MergedIntoId!.Value)))
            .ToList());
    }
}
/// <summary>
/// Справочник стран: дубли одной страны и коды не в alpha-3 (инцидент И-14,
/// docs/data-integrity.md §14).
///
/// Дубль тихий: на витрине обе записи выглядят одинаково (флаг рисуется по alpha-2),
/// расхождение видно только там, где код участвует в ПОИСКЕ — рекорды, будущие рейтинги,
/// сравнение стран. Пловцы, смотрящие на «вторую» запись, не падают, а показывают пусто:
/// у «IL» их было 791, и никто не замечал два месяца.
///
/// Вход закрыт нормализацией в трёх find-or-create (<see cref="CountryCodes.Normalize"/>),
/// но справочник наполняют ещё и восстановленные дампы, поэтому проверка остаётся.
/// </summary>
public sealed class CountryDuplicateCheck(SwimmDbContext db) : IDataCheck
{
    public string Id => "countries.duplicate";
    public string Title => "Дубли и alpha-2 в справочнике стран";
    public string Description =>
        "Две записи одной страны (например «ISR» и «IL») или код не в alpha-3. Правило проекта — " +
        "alpha-3 в данных, alpha-2 только флагам: рекорды и рейтинги ищут по коду, и половина " +
        "пловцов страны молча выпадает из среза. Чинится склейкой FK (Swimmers, Clubs, Results, " +
        "HubGroups, Competitions) на канон и удалением дубля.";
    public DataCheckSeverity Severity => DataCheckSeverity.Error;

    public async Task<DataCheckOutcome> RunAsync(CancellationToken ct = default)
    {
        var countries = await db.Countries.AsNoTracking()
            .Select(c => new { c.Id, c.CountryCode, c.CountryName })
            .ToListAsync(ct);

        // Канон группы — запись с alpha-3 кодом; на неё и указывает подсказка «склеить в».
        var groups = countries
            .GroupBy(c => CountryCodes.Normalize(c.CountryCode))
            .ToDictionary(g => g.Key, g => g.ToList());

        var items = new List<DataCheckItem>();
        foreach (var (code, rows) in groups.OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var canonical = rows.FirstOrDefault(r => CountryCodes.LooksAlpha3(r.CountryCode)) ?? rows[0];

            foreach (var row in rows)
            {
                var isDuplicate = rows.Count > 1 && row.Id != canonical.Id;
                var isAlpha2 = !CountryCodes.LooksAlpha3(row.CountryCode);
                if (!isDuplicate && !isAlpha2) continue;

                // Счётчики ссылок — чтобы было видно масштаб до склейки, как в §14.
                var swimmers = await db.Swimmers.CountAsync(x => x.CountryId == row.Id, ct);
                var results = await db.Results.CountAsync(x => x.CountryId == row.Id, ct);
                var clubs = await db.Clubs.CountAsync(x => x.CountryId == row.Id, ct);

                items.Add(new DataCheckItem(
                    "Country", row.Id,
                    isDuplicate
                        ? $"#{row.Id} «{row.CountryCode}» — дубль страны «{canonical.CountryCode}» (#{canonical.Id})"
                        : $"#{row.Id} «{row.CountryCode}» — код не alpha-3",
                    $"пловцов {swimmers}, результатов {results}, клубов {clubs}" +
                        (isDuplicate ? $"; склеить в #{canonical.Id} ({code})" : ""),
                    "/Admin/Db"));
            }
        }

        return items.Count == 0 ? DataCheckOutcome.Empty : new DataCheckOutcome(items.Count, items.Take(50).ToList());
    }
}
