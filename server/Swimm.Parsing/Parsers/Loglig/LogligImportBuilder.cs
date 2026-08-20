using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Parsing.Helpers;
using Swimm.Domain;
using Swimm.Parsing.Models;

namespace Swimm.Parsing.Parsers.Loglig;

/// <summary>
/// Строки пособытийного источника loglig → JSON импорта (шаг 3, docs/data-integrity.md §10).
///
/// Главное отличие от PDF-ветки: у каждой строки известен <c>Round</c>, и он едет в ключ
/// upsert — утренний зачёт возрастных групп и вечерний финал перестают схлопываться.
/// Эстафетные события СОЗНАТЕЛЬНО пропускаются: страница печатает команду, но не её состав,
/// и эстафеты остаются от PDF-импорта (решение Влада; в импорт идёт флаг PreserveRelays,
/// иначе «удалить лишнее» их снесло бы).
/// </summary>
public sealed class LogligImportBuilder : ILogligImportBuilder
{
    public string BuildResultsJson(
        IReadOnlyList<LogligEventResultsDto> events,
        LogligImportContext context,
        Func<LogligResultRowDto, (string LastName, string FirstName)> resolveName)
    {
        var eventYear = AgeGroupHelper.ExtractYearFromDateString(context.Date);
        var withFinal = DisciplinesWithFinal(events);
        var rows = new List<Result>();

        foreach (var ev in events)
        {
            if (ev.IsRelay) continue;

            foreach (var r in ev.Rows)
            {
                var (lastName, firstName) = resolveName(r);
                var birthYear = r.BirthYear ?? 0;
                var age = birthYear > 0 ? eventYear - birthYear : 0;

                // Пол и возрастная полоса — из СЕКЦИИ строки: у вечернего финала шапка
                // события открытая («נשים 13-99»), а очки и медали считаются по секциям
                // внутри него («גמר - בנות 14»).
                // Секция «כללי» — общий финал без возрастной категории: очков и медалей он
                // не приносит (Р43), поэтому едет отдельным раундом, а не «финалом».
                var isOpenSection = r.Category.Contains("כללי", StringComparison.Ordinal);
                var round = isOpenSection ? ResultRounds.FinalOpen : r.Round;

                var (sectionGender, sectionAge) = ParseCategory(r.Category);
                var gender = sectionGender != "none" ? sectionGender : ev.Gender;
                var eventStyleAge = sectionAge.Length > 0 ? sectionAge
                    : age > 0 ? age.ToString(CultureInfo.InvariantCulture)
                    : ev.AgeBand;

                rows.Add(new Result(
                    Country: context.Country,
                    Competition: context.CompetitionName,
                    IsMasters: "false",
                    IsAward: context.IsAward,
                    AgeGroup: age > 0 ? AgeGroupHelper.GetAgeGroup(age) : string.Empty,
                    Date: context.Date,
                    Event: $"{ev.Distance} {ev.StyleName} - {r.Category}".Trim(),
                    EventStyleName: ev.StyleName,
                    EventStyleLen: ev.Distance,
                    EventStyleGender: gender,
                    EventStyleAge: eventStyleAge,
                    PoolType: context.PoolType,
                    Position: r.Position,
                    Heat: r.Heat,
                    Lane: r.Lane,
                    LastName: lastName,
                    FirstName: firstName,
                    LastNameEn: string.Empty,
                    FirstNameEn: string.Empty,
                    BirthYear: birthYear,
                    Club: r.Club,
                    ClubEn: string.Empty,
                    Time: r.Time ?? string.Empty,
                    TimeFail: r.Time is null,
                    TimeFailNote: r.FailNote,
                    InternationalPoints: r.InternationalPoints,
                    Note: null,
                    IsRelay: false,
                    RelayTeamName: null,
                    RelaySwimmersName: null,
                    RelaySwimmers: null,
                    EventCategory: null,
                    HeatType: HeatTypeOf(r.Round, withFinal.Contains(DisciplineKey(ev, r.Category))),
                    Round: round,
                    // Пустая ячейка «ניקוד קבוצתי» у пособытийного источника означает
                    // «организатор за этот заплыв не заплатил», а НЕ «данных нет»: колонка
                    // в таблице есть всегда. Пишем 0, иначе сверка не увидит самый частый
                    // случай расхождения — мы платим, а официально ноль (секция «כללי»).
                    OfficialClubPoints: r.ClubPoints ?? 0));
            }
        }

        return JsonSerializer.Serialize(rows);
    }

    /// <summary>
    /// Дисциплины, у которых есть финальный раунд. Нужно, чтобы отличить предварительные
    /// от «утра, ставшего результатом»: регламент отменяет финал, если в нём осталось
    /// ≤ 2 участника («הגמר יבוטל»), и тогда мокдамот — единственный заплыв дисциплины.
    /// </summary>
    private static HashSet<string> DisciplinesWithFinal(IReadOnlyList<LogligEventResultsDto> events) =>
    [
        .. events
            .Where(ev => !ev.IsRelay)
            .SelectMany(ev => ev.Rows
                .Where(r => r.Round is LogligRounds.Final or LogligRounds.TimedFinal)
                .Select(r => DisciplineKey(ev, r.Category)))
    ];

    private static string DisciplineKey(LogligEventResultsDto ev, string category) =>
        $"{ev.StyleName}|{ev.Distance}|{category}";

    /// <summary>
    /// Наш ВЫВОД об отборе, который читает правило Р34 «место в предварительном — не награда»
    /// (и <c>FinalsOnly</c> у High Point). Раунд источника остаётся в <c>Round</c> как факт.
    ///
    /// Предварительные помечаются <c>prelim</c> ТОЛЬКО когда у дисциплины есть финал: если
    /// финал отменён (в нём ≤ 2 участника), медали и очки даёт утренний заплыв, и гасить его
    /// правилом Р34 нельзя. Обе разновидности финала — вечерний и утренний прямой — для
    /// этого правила одинаковы: и там и там место есть награда.
    /// </summary>
    private static string? HeatTypeOf(string round, bool disciplineHasFinal) => round switch
    {
        LogligRounds.Prelim => disciplineHasFinal ? "prelim" : null,
        LogligRounds.Final or LogligRounds.TimedFinal => "final",
        _ => null
    };

    /// <summary>«בנות 14» → female + «14»; «כללי» — пола и возраста нет, берём из шапки события.</summary>
    private static (string Gender, string Age) ParseCategory(string category)
    {
        var tokens = category.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0) return ("none", string.Empty);
        return (HebrewTextHelper.NormalizeGenderHE(tokens[0]), string.Join(' ', tokens.Skip(1)).Trim());
    }
}
