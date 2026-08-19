using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Parsing.Helpers;
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
                    HeatType: null,
                    Round: r.Round));
            }
        }

        return JsonSerializer.Serialize(rows);
    }

    /// <summary>«בנות 14» → female + «14»; «כללי» — пола и возраста нет, берём из шапки события.</summary>
    private static (string Gender, string Age) ParseCategory(string category)
    {
        var tokens = category.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0) return ("none", string.Empty);
        return (HebrewTextHelper.NormalizeGenderHE(tokens[0]), string.Join(' ', tokens.Skip(1)).Trim());
    }
}
