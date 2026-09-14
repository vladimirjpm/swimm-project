using System.Reflection;
using System.Reflection.Emit;

namespace Swimm.Tests;

/// <summary>
/// Разбор IL для стражей «где в коде зовут X» (кэш: <see cref="CacheValueRulesTests"/>, места
/// сужения: <see cref="CacheRowsSitesTests"/>). Места ищутся по IL, а не списком: новый вызов
/// попадает под проверку в момент добавления. Асинхронные методы и лямбды — отдельные
/// сгенерированные классы, они тоже обходятся.
/// </summary>
internal static class IlCalls
{
    /// <summary>Все вызовы методов, для которых <paramref name="match"/> истинно, в типах сборок.</summary>
    public static List<(MethodBase Caller, MethodInfo Called)> Find(
        IEnumerable<Assembly> assemblies, Func<MethodInfo, bool> match)
    {
        var calls = new List<(MethodBase, MethodInfo)>();
        foreach (var assembly in assemblies)
            foreach (var type in assembly.GetTypes())
                foreach (var method in type.GetMethods(AllDeclared).Cast<MethodBase>()
                             .Concat(type.GetConstructors(AllDeclared)))
                    Collect(method, match, calls);
        return calls;
    }

    /// <summary>Внешний тип для сгенерированных компилятором (асинхронные методы, лямбды).</summary>
    public static Type OuterType(Type type)
    {
        while (type.DeclaringType is not null) type = type.DeclaringType;
        return type;
    }

    /// <summary>
    /// Место вызова как его написал человек: «Тип.Метод». У асинхронного метода IL живёт в
    /// <c>&lt;Метод&gt;d__5.MoveNext</c>, у лямбды — в <c>&lt;Метод&gt;b__0</c>: имя берётся из скобок.
    /// </summary>
    public static string SiteOf(MethodBase caller)
    {
        var name = Written(caller.Name) ?? Written(caller.DeclaringType!.Name) ?? caller.Name;
        return $"{OuterType(caller.DeclaringType!).Name}.{name}";

        static string? Written(string generated) =>
            generated.StartsWith('<') && generated.IndexOf('>') is > 1 and var end ? generated[1..end] : null;
    }

    private const BindingFlags AllDeclared =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static
        | BindingFlags.DeclaredOnly;

    private static readonly Dictionary<short, OpCode> OpCodesByValue = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(f => (OpCode)f.GetValue(null)!)
        .ToDictionary(o => o.Value);

    private static void Collect(MethodBase method, Func<MethodInfo, bool> match, List<(MethodBase, MethodInfo)> into)
    {
        byte[]? il;
        try { il = method.GetMethodBody()?.GetILAsByteArray(); }
        catch { return; }
        if (il is null) return;

        var typeArgs = method.DeclaringType is { IsGenericType: true } dt ? dt.GetGenericArguments() : null;
        var methodArgs = method.IsGenericMethod ? method.GetGenericArguments() : null;

        var pos = 0;
        while (pos < il.Length)
        {
            short value = il[pos++];
            if (value == 0xFE && pos < il.Length) value = (short)(0xFE00 | il[pos++]);
            if (!OpCodesByValue.TryGetValue(value, out var op)) return; // не разобрали — не гадаем

            if (op.OperandType == OperandType.InlineMethod)
            {
                var token = BitConverter.ToInt32(il, pos);
                try
                {
                    if (method.Module.ResolveMethod(token, typeArgs, methodArgs) is MethodInfo called && match(called))
                        into.Add((method, called));
                }
                catch (ArgumentException) { /* токен из чужого контекста — пропускаем */ }
            }

            pos += OperandSize(op.OperandType, il, pos);
        }
    }

    private static int OperandSize(OperandType type, byte[] il, int pos) => type switch
    {
        OperandType.InlineNone => 0,
        OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
        OperandType.InlineVar => 2,
        OperandType.InlineI8 or OperandType.InlineR => 8,
        OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, pos),
        _ => 4,
    };
}
