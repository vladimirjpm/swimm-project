namespace Swimm.Application.Dtos;

/// <summary>
/// Пара для склейки: все связи дубля перевешиваются на канонический клуб.
/// <paramref name="Kind"/> — вид склейки (<see cref="ClubMergeKinds"/>), null = «Дубль».
/// </summary>
public sealed record ClubMergePair(int CanonicalId, int DuplicateId, string? Kind = null)
{
    /// <summary>Склейка-переименование: старое название уходит в историю клуба (ClubNames).</summary>
    public bool IsRename => Kind == ClubMergeKinds.Rename;
}

/// <summary>
/// Вид склейки клубов (docs/plans/swimmer-club-change-plan.md §5, Н2). Решает человек: по
/// данным дубль и переименование неотличимы — оба выглядят как два клуба с общими пловцами.
/// </summary>
public static class ClubMergeKinds
{
    /// <summary>Дубль: один клуб записан двумя названиями (мусор парсера, другое написание).</summary>
    public const string Duplicate = "duplicate";

    /// <summary>
    /// Переименование: клуб сменил название. Дубль = старое название, канон = новое. Старое
    /// название с годами использования попадает в <c>ClubNames</c>, перенесённые строки
    /// помнят его (<c>Results.ClubNameId</c>), а английское название дубля канону НЕ передаётся.
    /// </summary>
    public const string Rename = "rename";

    public static bool IsKnown(string? kind) => kind is null or Duplicate or Rename;
}

/// <summary>Итог обработки одной пары.</summary>
public sealed class ClubMergePairResult
{
    public int CanonicalId { get; init; }
    public int DuplicateId { get; init; }

    /// <summary>Вид склейки: duplicate | rename.</summary>
    public string Kind { get; init; } = ClubMergeKinds.Duplicate;

    /// <summary>merged | dry-run | conflict | error</summary>
    public string Status { get; set; } = "dry-run";

    /// <summary>Человекочитаемые действия («Results: 120 → canonical», …).</summary>
    public List<string> Actions { get; } = [];

    /// <summary>Причины отказа (обе стороны с официальной группой, синтетика, не найден…).</summary>
    public List<string> Conflicts { get; } = [];
}

/// <summary>Отчёт merge-прогона (dry-run по умолчанию — БД не меняется).</summary>
public sealed class ClubMergeReport
{
    public bool DryRun { get; init; }
    public List<ClubMergePairResult> Pairs { get; } = [];
}

/// <summary>
/// Отчёт догона цепочек склеек (Н4, <c>--flatten-club-merges</c>): какие надгробия смотрели
/// не на живой клуб и куда перевешены; какие прежние названия переехали на живого хозяина.
/// </summary>
public sealed class ClubChainFlattenReport
{
    public bool DryRun { get; init; }

    /// <summary>«#1329: 605 → 1372».</summary>
    public List<string> Moves { get; } = [];

    /// <summary>Прежние названия, чей хозяин оказался надгробием: «ClubNames #3: 605 → 1372».</summary>
    public List<string> NameMoves { get; } = [];

    /// <summary>Надгробия без живого конца (петля, обрыв) — чинит человек.</summary>
    public List<int> Broken { get; } = [];
}
