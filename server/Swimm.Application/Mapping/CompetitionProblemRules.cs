using Swimm.Application.Dtos;

namespace Swimm.Application.Mapping;

/// <summary>Причина, по которой у старта «есть проблема» (значок, фильтр «С ошибками», блок «Что не так»).</summary>
public enum CompetitionProblemKind
{
    /// <summary>Открытая находка Health с Severity = Error.</summary>
    HealthError,
    /// <summary>Открытая находка Health с Severity = Warning.</summary>
    HealthWarning,
    /// <summary>Строки, помеченные сомнительными («⚑ N», шаг 8).</summary>
    Suspect,
    /// <summary>Протокол неполный: видимая отметка Р114 или затянуты не все дни по сайту (Р115).</summary>
    Incomplete,
    /// <summary>Последняя доклейка промежуточных хоть у одного дня не удалась или прошла частично — повторить кнопкой.</summary>
    SplitsRetry,
}

/// <summary>
/// Всё, что правилу нужно о строке списка. Собирается <see cref="CompetitionProblemRules.From"/>
/// из того, что у строки уже есть, — разметка и фильтр одного условия не дублируют.
/// </summary>
public sealed record CompetitionProblemInput(
    int HealthErrors = 0,
    int HealthWarnings = 0,
    int SuspectCount = 0,
    bool Incomplete = false,
    bool SplitsNeedRetry = false);

/// <summary>
/// «У старта есть проблема» — одно правило на значок строки, фильтр <c>?filter=problems</c> и блок
/// «Что не так» в карточке /Admin/Competitions. Только вид: проверки, состояния строк и долг сезонов
/// оно не меняет. Тесты — <c>CompetitionProblemRulesTests</c>.
///
/// Находки Health считаются по соревнованию (<c>EntityType = 'Competition'</c>) и по его строкам
/// (<c>'Result'</c>); находки по пловцу (<c>'Swimmer'</c>) — НЕТ: пловец плывёт во многих стартах,
/// и одна находка зажгла бы значок у каждого. Info (Severity 0) не считается.
/// </summary>
public static class CompetitionProblemRules
{
    /// <summary>Значение <c>?filter=</c> страницы — «С ошибками».</summary>
    public const string Filter = "problems";

    /// <summary>Severity находки Health (<c>DataCheckSeverity</c>): 1 — Warning, 2 — Error.</summary>
    public const int WarningSeverity = (int)DataCheckSeverity.Warning;
    public const int ErrorSeverity = (int)DataCheckSeverity.Error;

    /// <summary>Причины по порядку важности: ошибки Health, предупреждения, сомнительные, неполный, промежуточные.</summary>
    public static IReadOnlyList<CompetitionProblemKind> Reasons(CompetitionProblemInput input)
    {
        var list = new List<CompetitionProblemKind>(5);
        if (input.HealthErrors > 0) list.Add(CompetitionProblemKind.HealthError);
        if (input.HealthWarnings > 0) list.Add(CompetitionProblemKind.HealthWarning);
        if (input.SuspectCount > 0) list.Add(CompetitionProblemKind.Suspect);
        if (input.Incomplete) list.Add(CompetitionProblemKind.Incomplete);
        if (input.SplitsNeedRetry) list.Add(CompetitionProblemKind.SplitsRetry);
        return list;
    }

    /// <summary>Есть ли у старта хоть одна причина.</summary>
    public static bool HasProblem(CompetitionProblemInput input) => Reasons(input).Count > 0;

    /// <summary>Вход правила из строки списка (у события — сумма по дням).</summary>
    public static CompetitionProblemInput From(UnifiedCompetitionRowDto u)
    {
        var (errors, warnings) = Totals(u.Health);
        var db = u.Db;
        var suspects = db?.Single?.SuspectCount ?? db?.SuspectCount ?? 0;
        // Р114 — видимая отметка (принятая уже снята в проекции списка); Р115 — правило состояний
        // само ставит PulledDays/SiteDays, когда дней в базе меньше, чем по сайту, и не принято.
        var partial = db != null && (db.Single != null ? db.Single.PartialResults != null : db.PartialDayCount > 0);
        var shortDays = u.Status is { PulledDays: not null, SiteDays: not null };
        return new CompetitionProblemInput(errors, warnings, suspects, partial || shortDays, u.SplitsNeedRetry);
    }

    /// <summary>Есть ли проблема у строки списка — то, чем режет фильтр.</summary>
    public static bool HasProblem(UnifiedCompetitionRowDto u) => HasProblem(From(u));

    /// <summary>Сколько открытых ошибок и предупреждений в разбивке по проверкам.</summary>
    public static (int Errors, int Warnings) Totals(IEnumerable<CompetitionHealthCount> counts)
    {
        int e = 0, w = 0;
        foreach (var c in counts)
        {
            if (c.Severity >= ErrorSeverity) e += c.Count;
            else if (c.Severity == WarningSeverity) w += c.Count;
        }
        return (e, w);
    }

    /// <summary>
    /// Разбивки нескольких соревнований (дней события) — в одну: одна проверка с одной severity —
    /// одна строка, числа складываются. Порядок: ошибки первыми, дальше по числу.
    /// </summary>
    public static IReadOnlyList<CompetitionHealthCount> Merge(IEnumerable<CompetitionHealthCount> counts) =>
        counts
            .Where(c => c.Severity >= WarningSeverity && c.Count > 0)
            .GroupBy(c => (c.CheckId, Sev: Math.Min(c.Severity, ErrorSeverity)))
            .Select(g => new CompetitionHealthCount(g.Key.CheckId, g.Key.Sev, g.Sum(x => x.Count)))
            .OrderByDescending(c => c.Severity).ThenByDescending(c => c.Count).ThenBy(c => c.CheckId, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// Подсказка значка: «Название проверки — N» по строке на проверку нужной severity.
    /// Нет названия (проверку убрали из реестра) — id проверки.
    /// </summary>
    public static string Hint(IEnumerable<CompetitionHealthCount> counts, int severity, IReadOnlyDictionary<string, string>? titles) =>
        string.Join("\n", counts
            .Where(c => severity >= ErrorSeverity ? c.Severity >= ErrorSeverity : c.Severity == severity)
            .Select(c => $"{(titles != null && titles.TryGetValue(c.CheckId, out var t) ? t : c.CheckId)} — {c.Count}"));
}
