using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Кандидаты на склейку клубов (docs/tasks/club-merge-plan.md, фаза B). Реальных клубов
/// сотни — попарное сравнение в памяти дешёвое. Три эвристики по убыванию уверенности:
/// 1) мусорный хвост парсера («… DNS», «… 4.4 SW /») при существующем «чистом» клубе —
///    sure, можно лить пачкой; 2) пересечение пловцов (≥3 общих и ≥30% меньшего клуба) —
///    ловит кросс-скриптовые пары без транслитерации; 3) Левенштейн ≤ 1 внутри одного
///    скрипта (опечатки/приставки). Синтетика (SYNTH%) и псевдоклубы Maccabiah исключены.
/// </summary>
public class ClubDedupService(SwimmDbContext db) : IClubDedupService
{
    /// <summary>
    /// Псевдоклубы Maccabiah: в протоколе вместо клуба страна/сборная. НЕ дубли —
    /// из кандидатов исключаются (решение по плану: оставить в БД, не мержить).
    /// </summary>
    public static readonly IReadOnlySet<string> PseudoClubNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "USA", "Israel", "Germany", "Ukraine", "Mexico", "Brazil",
        "M25", "Maccabiah MIX",
    };

    // Хвост заметки дисквалификации, прилипший к названию клуба в старых импортах
    // (до фикса парсера в фазе A): "DNS", "DNF", "NS", "DQ", "SW", "/", "4.4" и их
    // цепочки (включая "10.2 SW / DNF").
    private static readonly Regex TailRx = new(
        @"(\s+(DNS|DNF|NS|DQ|SW|/|\d+\.\d+))+\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Название без мусорного хвоста (или исходное, если хвоста нет).</summary>
    public static string StripGarbageTail(string name) => TailRx.Replace(name, "").Trim();

    private static bool HasHebrew(string s) => s.Any(c => c is >= 'א' and <= 'ת');

    private sealed record ClubInfo(int Id, string Name, string NameEn, int ResultCount);

    public async Task<ClubDedupReport> FindCandidatesAsync(CancellationToken ct = default)
    {
        // Счётчики и пары «клуб-пловец» считаем ТОЛЬКО по реальным клубам: синтетика
        // (SYNTH%) держит миллионы Results, скан по ним кладёт запрос на десятки секунд.
        // Пустое название — технический «клуб без клуба» (результаты, где клуб не был
        // указан в протоколе): это не дубль ничего, в кандидаты не попадает.
        var rawClubs = await db.Clubs.AsNoTracking()
            .Where(c => !c.Name.StartsWith("SYNTH") && c.Name.Trim() != "")
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

        var real = clubs.Where(c => !PseudoClubNames.Contains(c.Name.Trim())).ToList();
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

        void Add(ClubInfo canon, ClubInfo dup, string heuristic, int sharedSwimmers, bool sure)
        {
            if (!seen.Add((Math.Min(canon.Id, dup.Id), Math.Max(canon.Id, dup.Id))))
                return;
            if (!dupUsed.Add(dup.Id)) return;
            report.Candidates.Add(new ClubDedupCandidate(
                canon.Id, canon.Name, canon.NameEn, canon.ResultCount,
                dup.Id, dup.Name, dup.NameEn, dup.ResultCount,
                heuristic, sharedSwimmers, sure));
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

        // Уверенные сверху, затем по эвристике и имени.
        report.Candidates.Sort((x, y) =>
            (!x.Sure, x.Heuristic, x.CanonicalName).CompareTo((!y.Sure, y.Heuristic, y.CanonicalName)));

        return report;
    }
}
