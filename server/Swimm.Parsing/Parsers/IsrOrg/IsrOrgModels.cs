using System.Collections.Generic;
using Swimm.Parsing.Models;

namespace Swimm.Parsing.Parsers.IsrOrg;

public record IsrOrgResult(
    string Country,
    object? Position,
    int Heat,
    int Lane,
    string LastName,
    string FirstName,
    int BirthYear,
    string Club,
    string? Time,
    string? TimeFailNote,
    int InternationalPoints,
    bool? IsRelay,
    string? RelayTeamName,
    string? RelaySwimmersName,
    List<RelaySwimmer>? RelaySwimmers
)
{
    /// <summary>
    /// Имя собрано из двух строк-фрагментов вокруг строки данных (перенос в ячейке, И-48);
    /// склейка уже в <see cref="LastName"/>. Нужно паре HE+EN: колонку переноса (имя или
    /// фамилия) и пробел решает ивритская строка.
    /// </summary>
    public IsrOrgNameWrap? NameWrap { get; init; }
}

/// <summary>Две части перенесённого имени: строка над данными и строка под ними.</summary>
public sealed record IsrOrgNameWrap(string Before, string After);

public record IsrOrgCompetitionResult(
    string Competition,
    string AgeGroup,
    string Date,
    string Event,
    string EventStyleName,
    string EventStyleLen,
    string EventStyleGender,
    string EventStyleAge,
    string PoolType,
    List<IsrOrgResult> Results
);
