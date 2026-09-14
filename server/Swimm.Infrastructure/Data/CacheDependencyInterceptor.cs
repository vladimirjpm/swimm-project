using System.Collections.Concurrent;
using System.Data.Common;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Swimm.Infrastructure.Services;

namespace Swimm.Infrastructure.Data;

/// <summary>
/// Метки кэша ставятся САМИ (docs/plans/cache-tags-plan.md, К3): каждый SQL-запрос, выполненный
/// внутри сборки записи кэша (<see cref="CacheBuildScope"/>), отмечает в ней таблицы, которых
/// коснулся, — <c>table:Results</c>, <c>table:Records</c>… Вне сборки перехватчик ничего не делает.
///
/// ⚠ Именно перехват КОМАНДЫ, а не <c>IQueryExpressionInterceptor</c>: тот срабатывает на
/// компиляции запроса, а скомпилированный запрос EF кэширует — со второго выполнения он молчит,
/// и метка потерялась бы. Команда же исполняется каждый раз.
///
/// Таблицы ищутся в тексте SQL по закавыченным именам из модели EF (Npgsql кавычит PascalCase
/// всегда). Лишнее совпадение — колонка, названная как таблица, — даёт лишний сброс, а не
/// недосброс, и это безопасно.
///
/// Служебные колонки (docs/plans/cache-row-precision-plan.md §2.6, К4б.6) — так же: в SQL есть и
/// таблица <c>"T"</c>, и её служебная колонка <c>"C"</c> → <c>col:T.C</c>. EF называет каждую
/// колонку, которую читает, поэтому не названная — не прочитана. Та же колонка у другой таблицы
/// запроса (<c>"UpdatedAt"</c> клуба рядом с группами) даёт лишний сброс, а не ошибку;
/// <c>SELECT *</c> и <c>"x".*</c> считаются чтением всех служебных колонок упомянутых таблиц.
/// Метки <c>col:</c> ставятся при любом положении выключателя <c>CacheColumnPrecision</c> —
/// он действует на запись.
/// </summary>
public sealed class CacheDependencyInterceptor : DbCommandInterceptor
{
    private static readonly Regex QuotedIdentifier = new("\"([^\"]+)\"", RegexOptions.Compiled);

    // Чтение всех колонок: SELECT *, SELECT DISTINCT *, ", *", "x".*. COUNT(*) колонок не читает и
    // сюда не попадает; лишнее совпадение (звёздочка в строковой константе) — лишний сброс.
    private static readonly Regex Star = new(
        @"(?:\bSELECT\s+(?:DISTINCT\s+)?|,\s*|\.)\*", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Имена таблиц модели и её служебные колонки по таблицам.</summary>
    private sealed record ModelNames(HashSet<string> Tables, IReadOnlyDictionary<string, string[]> ServiceColumns);

    // Один раз на модель (у SwimmDbContext и SwimmReadDbContext свои).
    private static readonly ConcurrentDictionary<IModel, ModelNames> NamesByModel = new();

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        Record(command, eventData);
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        Record(command, eventData);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        Record(command, eventData);
        return result;
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        Record(command, eventData);
        return ValueTask.FromResult(result);
    }

    private static void Record(DbCommand command, CommandEventData eventData)
    {
        var scope = CacheBuildScope.Current;
        if (scope is null || eventData.Context is null) return;

        var names = NamesByModel.GetOrAdd(eventData.Context.Model, Names);
        var sql = command.CommandText;
        var identifiers = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in QuotedIdentifier.Matches(sql)) identifiers.Add(match.Groups[1].Value);

        bool? star = null;
        foreach (var name in identifiers)
        {
            if (!names.Tables.Contains(name)) continue;
            // table:T — или, в блоке сужения этой сборки, метки строк корня + anyrow:T (К4б.3);
            // плюс col:T.C на служебные колонки таблицы, названные в том же SQL (К4б.6).
            string[]? columns = null;
            if (names.ServiceColumns.TryGetValue(name, out var service))
                columns = (star ??= Star.IsMatch(sql)) ? service : service.Where(identifiers.Contains).ToArray();
            scope.TouchTable(name, columns);
        }
    }

    private static ModelNames Names(IModel model) => new(
        model.GetEntityTypes()
            .SelectMany(e => new[] { e.GetTableName(), e.GetViewName() })
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal),
        CacheServiceColumns.Of(model).ByTable);
}
