using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;

namespace Swimm.Application.Validation;

/// <summary>
/// Что можно класть в кэш так, чтобы переезд на Redis был заменой регистрации, а не
/// переписыванием (docs/plans/cache-tags-plan.md §4-2).
///
/// В памяти кэш хранит САМ ОБЪЕКТ и проглотит что угодно. Redis хранит байты: значение уходит
/// через System.Text.Json и обратно, и всё, что этот круг не переживает, в Redis молча
/// превратится в пустоту. Поэтому правило проверяется уже сейчас, на памяти, — при каждой
/// записи (<c>MemoryCacheService</c>) и тестом по всем местам вызова кэша.
///
/// Запрещено:
/// <list type="bullet">
/// <item><b>кортежи</b> — у <c>ValueTuple</c> только поля, а System.Text.Json пишет поля лишь по
/// флагу: <c>(List&lt;ResultDto&gt;, bool, int)</c> превращается в <c>{}</c>;</item>
/// <item><b>публичные поля</b> вместо свойств — по той же причине;</item>
/// <item><b>сущности EF</b> (<c>Swimm.Domain.Entities</c>) — навигации тянут граф с циклами, а
/// сущность не контракт: переименовали колонку — кэш на Redis перестал читаться;</item>
/// <item><b>делегаты, <c>IQueryable</c>, <c>Task</c>, <c>object</c>, интерфейсы-не-коллекции</b> —
/// сериализатору нечего записать или не во что восстановить.</item>
/// </list>
/// </summary>
public static class CacheValueRules
{
    private static readonly ConcurrentDictionary<Type, string?> Verdicts = new();

    /// <summary>null — тип можно класть в кэш; иначе причина, почему нельзя.</summary>
    public static string? Violation(Type type) =>
        Verdicts.GetOrAdd(type, t => Check(t, new HashSet<Type>()));

    /// <summary>Бросает, если тип нельзя класть в кэш. Проверка одна на тип — дальше из словаря.</summary>
    public static void EnsureStorable(Type type)
    {
        var violation = Violation(type);
        if (violation is not null)
            throw new InvalidOperationException(
                $"Тип {Describe(type)} нельзя класть в кэш: {violation}. " +
                "Положи DTO/record со свойствами (docs/plans/cache-tags-plan.md §4-2).");
    }

    private static string? Check(Type type, HashSet<Type> visiting)
    {
        var underlying = Nullable.GetUnderlyingType(type);
        if (underlying is not null) return Check(underlying, visiting);

        if (IsScalar(type)) return null;

        if (IsTuple(type))
            return $"{Describe(type)} — кортеж: у него только поля, и System.Text.Json пишет его как {{}}";

        if (type.Namespace == "Swimm.Domain.Entities")
            return $"{Describe(type)} — сущность EF, а не DTO";

        if (typeof(Delegate).IsAssignableFrom(type) || typeof(Task).IsAssignableFrom(type)
            || typeof(IQueryable).IsAssignableFrom(type))
            return $"{Describe(type)} — не данные";

        if (type == typeof(object))
            return "object — сериализатору не во что восстановить значение";

        if (type.IsArray)
            return Check(type.GetElementType()!, visiting);

        if (typeof(IEnumerable).IsAssignableFrom(type))
        {
            // Коллекции (List/IReadOnlyList/HashSet/Dictionary…) — проверяем их элементы.
            var elementTypes = CollectionElementTypes(type);
            if (elementTypes is null)
                return $"{Describe(type)} — коллекция без известного типа элемента";
            foreach (var element in elementTypes)
                if (Check(element, visiting) is { } inner) return inner;
            return null;
        }

        if (type.IsInterface || type.IsAbstract)
            return $"{Describe(type)} — интерфейс/абстрактный тип: сериализатору не во что восстановить";

        // DTO: публичные поля не сериализуются, свойства проверяем вглубь (сущность внутри
        // DTO так же непереносима, как и снаружи). Цикл ссылок типов — не ошибка.
        if (!visiting.Add(type)) return null;

        var field = type.GetFields(BindingFlags.Public | BindingFlags.Instance).FirstOrDefault();
        if (field is not null)
            return $"{Describe(type)}.{field.Name} — публичное поле: System.Text.Json пишет только свойства";

        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (prop.GetIndexParameters().Length > 0) continue;
            if (Check(prop.PropertyType, visiting) is { } inner)
                return $"{Describe(type)}.{prop.Name}: {inner}";
        }
        return null;
    }

    private static bool IsScalar(Type type) =>
        type.IsPrimitive || type.IsEnum
        || type == typeof(string) || type == typeof(decimal) || type == typeof(Guid)
        || type == typeof(DateTime) || type == typeof(DateTimeOffset)
        || type == typeof(DateOnly) || type == typeof(TimeOnly) || type == typeof(TimeSpan);

    private static bool IsTuple(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition().FullName is { } name
        && (name.StartsWith("System.ValueTuple`", StringComparison.Ordinal)
            || name.StartsWith("System.Tuple`", StringComparison.Ordinal));

    /// <summary>Типы элементов коллекции: из IDictionary&lt;K,V&gt; — K и V, из IEnumerable&lt;T&gt; — T.</summary>
    private static Type[]? CollectionElementTypes(Type type)
    {
        var interfaces = type.IsInterface ? type.GetInterfaces().Append(type) : type.GetInterfaces();
        var dictionary = interfaces.FirstOrDefault(i => i.IsGenericType
            && (i.GetGenericTypeDefinition() == typeof(IDictionary<,>)
                || i.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>)));
        if (dictionary is not null) return dictionary.GetGenericArguments();

        var enumerable = interfaces.FirstOrDefault(i =>
            i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));
        return enumerable?.GetGenericArguments();
    }

    private static string Describe(Type type) =>
        type.IsGenericType
            ? $"{type.Name[..type.Name.IndexOf('`')]}<{string.Join(", ", type.GetGenericArguments().Select(Describe))}>"
            : type.Name;
}
