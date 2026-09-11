using System.Collections.Concurrent;
using System.Data.Common;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Swimm.Application.Constants;
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
/// </summary>
public sealed class CacheDependencyInterceptor : DbCommandInterceptor
{
    private static readonly Regex QuotedIdentifier = new("\"([^\"]+)\"", RegexOptions.Compiled);

    // Имена таблиц модели — один раз на модель (у SwimmDbContext и SwimmReadDbContext свои).
    private static readonly ConcurrentDictionary<IModel, HashSet<string>> TablesByModel = new();

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

        var tables = TablesByModel.GetOrAdd(eventData.Context.Model, TableNames);
        foreach (Match match in QuotedIdentifier.Matches(command.CommandText))
        {
            var name = match.Groups[1].Value;
            if (tables.Contains(name)) scope.Touch(CacheTags.Table(name));
        }
    }

    private static HashSet<string> TableNames(IModel model) => model.GetEntityTypes()
        .SelectMany(e => new[] { e.GetTableName(), e.GetViewName() })
        .OfType<string>()
        .ToHashSet(StringComparer.Ordinal);
}
