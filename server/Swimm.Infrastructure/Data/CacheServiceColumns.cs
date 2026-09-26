using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Swimm.Domain.Entities;

namespace Swimm.Infrastructure.Data;

/// <summary>
/// Служебные колонки (docs/plans/cache-row-precision-plan.md §2.6, К4б.6) — те, что часто пишут и
/// мало кто читает: loglig-привязка пловца, штамп проверки качества, объединённые места, отметки
/// «обновлено». Правка, задевшая ТОЛЬКО их, сбрасывает не <c>table:T</c>, а <c>col:T.C</c>
/// (<see cref="CacheInvalidationInterceptor"/>), и падают лишь записи кэша, в SQL которых эта
/// колонка названа (<see cref="CacheDependencyInterceptor"/>). Loglig-задания и проверка качества
/// перестают ронять season-best, обзоры клубов и страницы групп.
///
/// Почему безопасно: EF называет в SQL каждую колонку, которую читает, — в SELECT, WHERE, ORDER BY.
/// Не назвал — не читал, и правка одной этой колонки его ответ не меняет. Вычисляемых колонок,
/// представлений и сырого SQL в модели нет (проверено 14.09.2026); <c>SELECT *</c> перехватчик
/// чтения считает чтением всех служебных колонок.
///
/// Реестр короткий нарочно — узкое отступление от «по колонкам не дробим» (§3.3 cache-tags-plan),
/// а не новое правило. Колонка идёт сюда, если журнал сбросов на /Admin/Cache показал: её запись
/// часто выкидывает записи, которые её не читают. Проверка по модели (при первом обращении,
/// исключение — в тестах сразу): свойство есть; не ключ и не FK на корень сужения — на них
/// держатся метки строк; у корня нет FK на себя (склейка клуба <c>MergedIntoId</c>) — служебная
/// правка корня даёт метку только своей строки, а такого корня в его блоке читают и по этому FK;
/// имя колонки с заглавной буквой — Npgsql кавычит только такие, а чтение ищет колонки в кавычках.
/// </summary>
public static class CacheServiceColumns
{
    /// <summary>Реестр: сущность и её свойство. Имя колонки — из модели.</summary>
    public static IReadOnlyList<(Type Entity, string Property)> Properties { get; } =
    [
        // Loglig-привязка: подсказка пользователя, суточные задания, штамп после импорта и забора
        // стартовых протоколов. Её показывает страница пловца — остальные витрины её не читают.
        (typeof(Swimmer), nameof(Swimmer.LogligId)),
        (typeof(Swimmer), nameof(Swimmer.LogligIdStatus)),
        (typeof(Swimmer), nameof(Swimmer.LogligIdSource)),
        (typeof(Swimmer), nameof(Swimmer.LogligIdSuggestedByUserId)),
        (typeof(Swimmer), nameof(Swimmer.LogligIdSuggestedAt)),
        (typeof(Swimmer), nameof(Swimmer.LogligIdVerifiedAt)),
        // Штамп «проверка качества была» (SuspectResultService) — нужен только списку в админке.
        (typeof(Competition), nameof(Competition.QualityScannedAt)),
        // Объединённые места (CombinedPlaceCalculator) — их показывает протокол с Combine All Results.
        (typeof(ResultRecord), nameof(ResultRecord.CombinedPlace)),
        (typeof(ResultRecord), nameof(ResultRecord.IsBestResult)),
        (typeof(ResultRecord), nameof(ResultRecord.BestTimeMs)),
        // «Обновлено»: TouchGroupAsync после каждой правки состава, вход через Google.
        (typeof(HubGroup), nameof(HubGroup.UpdatedAt)),
        (typeof(AppUser), nameof(AppUser.UpdatedAt)),
    ];

    /// <summary>Служебные колонки одной модели: для записи — по свойствам, для чтения — по таблицам.</summary>
    public sealed class Resolved
    {
        /// <summary>Служебное свойство → имя его колонки.</summary>
        public required IReadOnlyDictionary<IReadOnlyProperty, string> Columns { get; init; }

        /// <summary>Таблица → имена её служебных колонок (как в SQL).</summary>
        public required IReadOnlyDictionary<string, string[]> ByTable { get; init; }
    }

    private static readonly ConditionalWeakTable<IReadOnlyModel, Resolved> ByModel = new();

    /// <summary>
    /// Служебные колонки модели. Сущности реестра, которых в модели нет (контекст с другой моделью,
    /// пробные контексты тестов), пропускаются; свойство, которого нет у сущности модели, —
    /// исключение.
    /// </summary>
    public static Resolved Of(IReadOnlyModel model) => ByModel.GetValue(model, m => Resolve(m, Properties));

    /// <summary>Разбор реестра по модели с проверками — отдельно, чтобы тесты проверили отказы на своём списке.</summary>
    internal static Resolved Resolve(IReadOnlyModel model, IEnumerable<(Type Entity, string Property)> properties)
    {
        var columns = new Dictionary<IReadOnlyProperty, string>();
        var byTable = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (type, name) in properties)
        {
            if (model.FindEntityType(type) is not { } entity) continue;
            var property = entity.FindProperty(name)
                ?? throw new InvalidOperationException($"{type.Name}.{name} из реестра служебных колонок нет в модели");
            var table = entity.GetTableName()
                ?? throw new InvalidOperationException($"{type.Name} не отображён на таблицу — служебной колонке негде быть");

            if (property.IsKey())
                throw new InvalidOperationException($"{type.Name}.{name} — ключ: служебным он быть не может, на нём метки строк");
            if (property.GetContainingForeignKeys().Any(fk => CacheRowRoots.IsParentRoot(fk.PrincipalEntityType)))
                throw new InvalidOperationException(
                    $"{type.Name}.{name} — FK на корень сужения: служебной быть не может, по нему метки строк (§2.2 п. 2)");
            if (CacheRowRoots.IsRoot(entity) && entity.GetForeignKeys().Any(fk => fk.PrincipalEntityType == entity))
                throw new InvalidOperationException(
                    $"У корня {type.Name} FK на себя: в своём блоке его читают и по этому FK, а служебная правка " +
                    "даёт метку только своей строки — служебных колонок у такого корня быть не может");

            var column = property.GetColumnName();
            if (!column.Any(char.IsUpper))
                throw new InvalidOperationException(
                    $"Колонка {table}.{column} без заглавной буквы: Npgsql не возьмёт её в кавычки, и чтение её не увидит");

            columns[property] = column;
            if (!byTable.TryGetValue(table, out var list)) byTable[table] = list = [];
            list.Add(column);
        }
        return new Resolved
        {
            Columns = columns,
            ByTable = byTable.ToDictionary(t => t.Key, t => t.Value.ToArray(), StringComparer.Ordinal),
        };
    }
}
