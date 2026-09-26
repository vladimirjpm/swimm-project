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
///
/// Корень «только своей строки» (<see cref="OwnRowOnly"/>) — у него метку строки сбрасывает лишь
/// запись самой строки, а FK на него меток не дают: под ним сужают только его собственные строки,
/// потомков — никогда (<c>CacheRows</c> с таблицами под таким корнем — исключение). Иначе каждая
/// запись со ссылкой на пользователя (группа по OwnerUserId, медиа, избранное) роняла бы страницы
/// групп, которыми он управляет, хотя ни одна из них этих потомков не читает.
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
        // Пользователь (26.09.2026): страница группы читает строки своих владельца и админов
        // (порядок состава, HubGroupRosterOrder) — таблицей она сбрасывалась бы на каждую
        // регистрацию и правку любого пользователя (К4б.0).
        typeof(AppUser),
    };

    /// <summary>Корни «только своей строки»: FK на них меток строк не дают.</summary>
    public static IReadOnlySet<Type> OwnRowOnly { get; } = new HashSet<Type> { typeof(AppUser) };

    public static bool IsRoot(IReadOnlyEntityType type) => Types.Contains(type.ClrType);

    /// <summary>Корень, чью метку строки сбрасывают и его потомки — по FK на него.</summary>
    public static bool IsParentRoot(IReadOnlyEntityType type) => IsRoot(type) && !OwnRowOnly.Contains(type.ClrType);
}
