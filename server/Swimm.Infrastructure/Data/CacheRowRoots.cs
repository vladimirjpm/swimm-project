using Microsoft.EntityFrameworkCore.Metadata;
using Swimm.Domain.Entities;

namespace Swimm.Infrastructure.Data;

/// <summary>
/// Корни сужения (docs/plans/cache-row-precision-plan.md §2.1) — таблицы, у строк которых есть
/// своя метка <c>row:T:id</c>. Её сбрасывает запись самой строки корня и её прямых потомков (FK
/// на корень), а зависит от неё запись кэша, сузившая чтение до строк этого корня.
///
/// Реестр короткий нарочно: метки строк прочих таблиц (свой id у <c>Results</c> никто не сужает)
/// лишь раздували бы каждое сохранение. Сужение по корню вне реестра — ошибка, а не метка, которую
/// никто никогда не сбросит.
///
/// Здесь, а не рядом с <c>CacheTags</c>: реестр — про типы сущностей EF, а Application про EF не
/// знает. Имена таблиц — из модели.
/// </summary>
public static class CacheRowRoots
{
    public static IReadOnlySet<Type> Types { get; } = new HashSet<Type>
    {
        typeof(HubGroup),
        typeof(Club),
        typeof(Swimmer),
        typeof(UserMedia),
        typeof(Relay),
    };

    public static bool IsRoot(IReadOnlyEntityType type) => Types.Contains(type.ClrType);
}
