using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Кандидаты на склейку клубов (docs/tasks/club-merge-plan.md, фаза B). Реальных клубов
/// сотни — попарное сравнение в памяти дешёвое. Эвристики по убыванию уверенности (same-name по Normalize, name-en и word-order — см. ниже):
/// 1) мусорный хвост парсера («… DNS», «… 4.4 SW /») при существующем «чистом» клубе —
///    sure, можно лить пачкой; 2) пересечение пловцов (≥3 общих и ≥30% меньшего клуба) —
///    ловит кросс-скриптовые пары без транслитерации; 3) Левенштейн ≤ 1 внутри одного
///    скрипта (опечатки/приставки). Синтетика (SYNTH%) и псевдоклубы Maccabiah исключены.
/// </summary>
/// Пары эвристики «общие пловцы» несут сводку «что знает loglig» про общих пловцов (Р112) — из
/// сохранённых ответов, без сети (<see cref="IClubPairLogligService"/>; в тестах может не быть).
public class ClubDedupService(SwimmDbContext db, IClubPairLogligService? loglig = null) : IClubDedupService
{
    // Хвост заметки дисквалификации, прилипший к названию клуба в старых импортах
    // (до фикса парсера в фазе A): "DNS", "DNF", "NS", "DQ", "SW", "/", "4.4" и их
    // цепочки (включая "10.2 SW / DNF").
    private static readonly Regex TailRx = new(
        @"(\s+(DNS|DNF|NS|DQ|SW|/|\d+\.\d+))+\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Название без мусорного хвоста (или исходное, если хвоста нет).</summary>
    public static string StripGarbageTail(string name) => TailRx.Replace(name, "").Trim();

    /// <summary>
    /// Ключ «набор слов»: Normalize, слова отсортированы. Пусто, если слов меньше двух
    /// (перестановка однословного названия ничего не значит).
    /// </summary>
    internal static string WordSetKey(string name)
    {
        var words = SwimmerDedupService.Normalize(name).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 2) return "";
        Array.Sort(words, StringComparer.Ordinal);
        return string.Join(' ', words);
    }

    private static bool HasHebrew(string s) => s.Any(c => c is >= 'א' and <= 'ת');

    private sealed record ClubInfo(int Id, string Name, string NameEn, int ResultCount);

    public async Task<ClubDedupReport> FindCandidatesAsync(CancellationToken ct = default)
    {
        // Счётчики и пары «клуб-пловец» считаем ТОЛЬКО по реальным клубам: синтетика
        // (SYNTH%) держит миллионы Results, скан по ним кладёт запрос на десятки секунд.
        // Исключены: синтетика, псевдоклубы (страна/сборная вместо клуба, флаг IsPseudo —
        // ставится импортом по справочнику Countries) и пустое название — технический
        // «клуб без клуба» (результаты, где клуб не был указан в протоколе).
        var rawClubs = await db.Clubs.AsNoTracking()
            // Склеенные (MergedIntoId) исключены: их связи давно переехали на приёмника,
            // предлагать их в кандидаты — значит склеивать пустышку второй раз.
            .Where(c => !c.Name.StartsWith("SYNTH") && !c.IsPseudo && c.Name.Trim() != ""
                        && c.MergedIntoId == null)
            .Select(c => new { c.Id, c.Name, c.NameEn })
            .ToListAsync(ct);
        var realClubIds = rawClubs.Select(c => c.Id).ToList();

        var resultCounts = (await db.Results.AsNoTracking()
                .Where(r => realClubIds.Contains(r.ClubId))
                .GroupBy(r => r.ClubId)
                .Select(g => new { ClubId = g.Key, N = g.Count() })
                .ToListAsync(ct))
            .ToDictionary(x => x.ClubId, x => x.N);

        var clubs = rawClubs
            .Select(c => new ClubInfo(c.Id, c.Name, c.NameEn, resultCounts.GetValueOrDefault(c.Id)))
            .ToList();

        var report = new ClubDedupReport { RealClubs = clubs.Count };

        var real = clubs;
        var byName = real
            .GroupBy(c => c.Name.Trim())
            .ToDictionary(g => g.Key, g => g.First());

        // Ключ пары (minId, maxId) — эвристики идут по убыванию уверенности,
        // повторная менее уверенная находка той же пары отбрасывается.
        var seen = new HashSet<(int, int)>();

        // «Развязанные» админом пары (Sys_DedupIgnoredPairs) — не показываем:
        // засеваем в seen, и Add() их молча пропустит по любой эвристике.
        var ignored = await db.DedupIgnoredPairs.AsNoTracking()
            .Where(p => p.EntityType == Swimm.Domain.Entities.DedupEntityType.Club)
            .Select(p => new { p.IdA, p.IdB })
            .ToListAsync(ct);
        foreach (var p in ignored) seen.Add((p.IdA, p.IdB));

        // Клуб может влиться только в ОДИН канон — вторая пара с тем же дублем лишь
        // роняет merge-вызов («один и тот же дубль в нескольких парах»). Эвристики
        // идут по убыванию уверенности, поэтому оставляем первую найденную.
        var dupUsed = new HashSet<int>();
        var canonUsed = new HashSet<int>();

        void Add(ClubInfo canon, ClubInfo dup, string heuristic, int sharedSwimmers, bool sure)
        {
            if (!seen.Add((Math.Min(canon.Id, dup.Id), Math.Max(canon.Id, dup.Id))))
                return;
            if (!dupUsed.Add(dup.Id)) return;
            canonUsed.Add(canon.Id);
            report.Candidates.Add(new ClubDedupCandidate(
                canon.Id, canon.Name, canon.NameEn, canon.ResultCount,
                dup.Id, dup.Name, dup.NameEn, dup.ResultCount,
                heuristic, sharedSwimmers, sure));
        }

        // ── 0. ОДИНАКОВОЕ имя у разных Id → sure ──────────────────────────
        // Самый очевидный дубль, а находился до 2026-08-01 только случайно: эвристика
        // суффикса требует хвоста, а левенштейн совпадающие имена явно пропускает
        // (na == nb → continue). Ловила их только эвристика по общим пловцам, да и та
        // молчала, если у дубля меньше трёх пловцов.
        // Массово это следы второго импорта: у канона имя на иврите без NameEn, у дубля
        // тот же Name и заполненный NameEn (65 групп, 68 дублей, 7793 результата).
        // Канон — у кого больше результатов (при равенстве меньший Id): к нему уедут
        // связи, а пустой NameEn канона merge дозаполнит из дубля.
        //
        // Группируем по Normalize(Name), а не по точному имени (Р-Ф0, 18.11.2026): иначе
        // «הפועל ג׳מים אילת» (гереш U+05F3) и «הפועל ג'מים אילת» (апостроф) не находились
        // ни здесь (разные строки), ни левенштейном (после Normalize они равны → continue).
        // Normalize сводит гереш/апостроф, финальные буквы, регистр и пробелы — это не
        // «похожие» имена, а одно и то же имя в разной записи, так что уверенность sure.
        foreach (var group in real.GroupBy(c => SwimmerDedupService.Normalize(c.Name)).Where(g => g.Count() > 1))
        {
            var ordered = group
                .OrderByDescending(c => c.ResultCount)
                .ThenBy(c => c.Id)
                .ToList();
            var canon = ordered[0];
            foreach (var dup in ordered.Skip(1))
                Add(canon, dup, "same-name", 0, sure: true);
        }

        // ── 0а. Латинское Name = NameEn ивритского клуба → sure ───────────
        // Р108 (19.11.2026): «Bnei Herzeliya» (1262, латиница в Name) — дубль «בני הרצליה»
        // (439, NameEn = «Bnei Herzeliya»). same-name сравнивает Name с Name и пару не видел,
        // склеили руками 18.11. Это то же имя клуба, только в другом поле: импорт EN-протокола
        // завёл клуб с английским названием в Name. Канон — ивритская запись (как у swimmers).
        // Берём только однозначные: тот же NameEn у двух ивритских клубов — пропуск.
        var hebrewByNameEn = real
            .Where(c => HasHebrew(c.Name) && !string.IsNullOrWhiteSpace(c.NameEn))
            .GroupBy(c => SwimmerDedupService.Normalize(c.NameEn!))
            .Where(g => g.Key.Length > 0 && g.Count() == 1)
            .ToDictionary(g => g.Key, g => g.Single());
        foreach (var dup in real.Where(c => !HasHebrew(c.Name)))
        {
            if (!hebrewByNameEn.TryGetValue(SwimmerDedupService.Normalize(dup.Name), out var canon)) continue;
            if (dupUsed.Contains(canon.Id) || canonUsed.Contains(dup.Id)) continue;
            Add(canon, dup, "name-en", 0, sure: true);
        }

        // ── 0б. ТОТ ЖЕ НАБОР СЛОВ в другом порядке → unsure ───────────────
        // «SWIM TLV» / «TLV SWIM», «Swimming Eilat» / «Eilat Swimming» (после Normalize).
        // Почему НЕ sure: порядок слов в названии клуба иногда несёт смысл (конструкт-
        // состояние иврита, «Hapoel X» против «X Hapoel» — теоретически разные клубы),
        // а ложная склейка необратима по смыслу (результаты уезжают к приёмнику). Совпадение
        // набора слов — сильный сигнал, но человек должен взглянуть. Минимум два слова:
        // у однословных порядок тривиален. Канон — у кого больше результатов (при равенстве
        // меньший Id). Клуб, уже ставший дублем (same-name), канон не берём, а канона
        // другой пары дублем не делаем — иначе получится цепочка склеек.
        foreach (var group in real
                     .Where(c => !dupUsed.Contains(c.Id))
                     .GroupBy(c => WordSetKey(c.Name))
                     .Where(g => g.Key.Length > 0 && g.Count() > 1))
        {
            var ordered = group
                .OrderByDescending(c => c.ResultCount)
                .ThenBy(c => c.Id)
                .ToList();
            var canon = ordered[0];
            var canonNorm = SwimmerDedupService.Normalize(canon.Name);
            foreach (var dup in ordered.Skip(1))
            {
                // Та же запись, что у канона, — это работа same-name, не порядка слов.
                if (SwimmerDedupService.Normalize(dup.Name) == canonNorm) continue;
                if (canonUsed.Contains(dup.Id)) continue;
                Add(canon, dup, "word-order", 0, sure: false);
            }
        }

        // ── 1. Мусорный суффикс: Name = Name другого клуба + хвост → sure ──
        foreach (var club in real)
        {
            var clean = StripGarbageTail(club.Name);
            if (clean.Length == 0 || clean == club.Name.Trim()) continue;
            if (byName.TryGetValue(clean, out var canon) && canon.Id != club.Id)
                Add(canon, club, "suffix", 0, sure: true);
        }

        // ── 2. Пересечение пловцов (Swimmers.ClubId + участники Results) ──
        var swimmersByClub = new Dictionary<int, HashSet<int>>();
        void AddSwimmer(int clubId, int swimmerId)
        {
            if (!swimmersByClub.TryGetValue(clubId, out var set))
                swimmersByClub[clubId] = set = [];
            set.Add(swimmerId);
        }

        var fromSwimmers = await db.Swimmers.AsNoTracking()
            .Where(s => s.ClubId != null && realClubIds.Contains(s.ClubId.Value))
            .Select(s => new { ClubId = s.ClubId!.Value, SwimmerId = s.Id })
            .ToListAsync(ct);
        foreach (var p in fromSwimmers) AddSwimmer(p.ClubId, p.SwimmerId);

        var fromResults = await db.Results.AsNoTracking()
            .Where(r => realClubIds.Contains(r.ClubId))
            .Select(r => new { r.ClubId, r.SwimmerId })
            .Distinct()
            .ToListAsync(ct);
        foreach (var p in fromResults) AddSwimmer(p.ClubId, p.SwimmerId);

        var withSwimmers = real.Where(c => swimmersByClub.ContainsKey(c.Id)).ToList();
        var sharedByPair = new Dictionary<(int, int), List<int>>();
        for (var i = 0; i < withSwimmers.Count; i++)
        for (var j = i + 1; j < withSwimmers.Count; j++)
        {
            var a = withSwimmers[i];
            var b = withSwimmers[j];
            var setA = swimmersByClub[a.Id];
            var setB = swimmersByClub[b.Id];
            var smaller = Math.Min(setA.Count, setB.Count);
            if (smaller < 3) continue;

            var shared = setA.Count(setB.Contains);
            if (shared < 3 || shared < 0.3 * smaller) continue;

            // Канон в кросс-скриптовой паре — ивритская запись (латиница уйдёт в NameEn),
            // иначе — у кого больше результатов (при равенстве — меньший Id).
            var aHe = HasHebrew(a.Name);
            var bHe = HasHebrew(b.Name);
            var (canon, dup) = aHe != bHe
                ? (aHe ? (a, b) : (b, a))
                : a.ResultCount != b.ResultCount
                    ? (a.ResultCount > b.ResultCount ? (a, b) : (b, a))
                    : a.Id < b.Id ? (a, b) : (b, a);
            sharedByPair[(canon.Id, dup.Id)] = setA.Where(setB.Contains).ToList();
            Add(canon, dup, "swimmers", shared, sure: false);
        }

        // ── 3. Левенштейн ≤ 1 внутри одного скрипта (опечатки, приставки) ──
        for (var i = 0; i < real.Count; i++)
        for (var j = i + 1; j < real.Count; j++)
        {
            var a = real[i];
            var b = real[j];
            if (HasHebrew(a.Name) != HasHebrew(b.Name)) continue;

            var na = SwimmerDedupService.Normalize(a.Name);
            var nb = SwimmerDedupService.Normalize(b.Name);
            if (na == nb || Math.Abs(na.Length - nb.Length) > 1) continue;
            if (SwimmerDedupService.Levenshtein(na, nb, 1) > 1) continue;

            var (canon, dup) = a.ResultCount != b.ResultCount
                ? (a.ResultCount > b.ResultCount ? (a, b) : (b, a))
                : a.Id < b.Id ? (a, b) : (b, a);
            Add(canon, dup, "levenshtein", 0, sure: false);
        }

        // Сводка loglig по общим пловцам (Р112) — только у пар «общие пловцы», из сохранённых
        // ответов: страница кандидатов в сеть не ходит, проверка — кнопкой у пары.
        if (loglig is not null)
        {
            var swimmerPairs = report.Candidates
                .Where(c => c.Heuristic == "swimmers" && sharedByPair.ContainsKey((c.CanonicalId, c.DuplicateId)))
                .Select(c => (c.CanonicalId, c.DuplicateId, (IReadOnlyCollection<int>)sharedByPair[(c.CanonicalId, c.DuplicateId)]))
                .ToList();
            var summaries = await loglig.SummarizeAsync(swimmerPairs, ct);
            for (var k = 0; k < report.Candidates.Count; k++)
            {
                var c = report.Candidates[k];
                if (c.Heuristic == "swimmers" && summaries.TryGetValue((c.CanonicalId, c.DuplicateId), out var s))
                    report.Candidates[k] = c with { Loglig = s };
            }
        }

        // Оба клуба в одних протоколах (Р113) — у всех пар: протокол, напечатавший их рядом как
        // разные команды, — главный довод против склейки. «Уверенность» эвристики не трогаем —
        // страница предупреждает и «Выбрать все уверенные» такие пары пропускает.
        var overlaps = await ClubProtocolOverlapQuery.LoadAsync(db,
            report.Candidates.Select(c => (c.CanonicalId, c.DuplicateId)).ToList(), ct);
        for (var k = 0; k < report.Candidates.Count; k++)
        {
            var c = report.Candidates[k];
            var withProtocols = c with { Protocols = overlaps.GetValueOrDefault((c.CanonicalId, c.DuplicateId)) };
            report.Candidates[k] = withProtocols with { Recommendation = ClubPairRecommendationRule.Decide(withProtocols) };
        }

        // Уверенные сверху, затем по эвристике и имени.
        report.Candidates.Sort((x, y) =>
            (!x.Sure, x.Heuristic, x.CanonicalName).CompareTo((!y.Sure, y.Heuristic, y.CanonicalName)));

        return report;
    }
}
