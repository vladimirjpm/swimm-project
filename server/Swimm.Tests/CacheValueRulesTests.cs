using System.Reflection;
using System.Reflection.Emit;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Application.Validation;
using Swimm.Infrastructure.Repositories;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Что можно класть в кэш, чтобы переезд на Redis был заменой регистрации
/// (docs/plans/cache-tags-plan.md §4-2, этап К2).
///
/// Главный тест здесь — <see cref="EveryCachedValueType_SurvivesRedis"/>: он находит ВСЕ места
/// вызова кэша в коде (по IL, а не списком), поэтому новый кортеж или сущность в кэше падает в
/// момент добавления, а не в день переезда на Redis, когда витрина молча опустеет.
/// </summary>
public class CacheValueRulesTests
{
    [Theory]
    [InlineData(typeof(ValueTuple<List<ResultDto>, bool, int>), "кортеж")]
    [InlineData(typeof(Tuple<int, string>), "кортеж")]
    [InlineData(typeof(List<Swimm.Domain.Entities.Record>), "сущность EF")]
    [InlineData(typeof(IReadOnlyList<Swimm.Domain.Entities.Record>), "сущность EF")]
    [InlineData(typeof(Func<int>), "не данные")]
    [InlineData(typeof(object), "object")]
    [InlineData(typeof(WithPublicField), "публичное поле")]
    [InlineData(typeof(DtoHoldingEntity), "сущность EF")]
    public void Refused(Type type, string reason)
    {
        var violation = CacheValueRules.Violation(type);

        Assert.NotNull(violation);
        Assert.Contains(reason, violation);
    }

    [Theory]
    [InlineData(typeof(int?))]
    [InlineData(typeof(string[]))]
    [InlineData(typeof(List<DateTime>))]
    [InlineData(typeof(List<ResultDto>))]
    [InlineData(typeof(IReadOnlyList<RecordDto>))]
    [InlineData(typeof(Dictionary<string, int[]>))]
    [InlineData(typeof(Dictionary<string, NationalAgeRecordRow>))]
    [InlineData(typeof(List<SeasonSwimRow>))]
    [InlineData(typeof(SelfReferencing))]
    public void Allowed(Type type) => Assert.Null(CacheValueRules.Violation(type));

    public sealed class WithPublicField { public int Count; }
    public sealed class DtoHoldingEntity { public List<Swimm.Domain.Entities.Club> Clubs { get; set; } = []; }
    public sealed class SelfReferencing { public List<SelfReferencing> Children { get; set; } = []; }

    /// <summary>
    /// Все конкретные типы, которые код кладёт в кэш или берёт из него, — по вызовам
    /// <see cref="ICacheService"/> в IL сборок Infrastructure и Application (асинхронные методы и
    /// лямбды — это отдельные сгенерированные классы, они тоже обходятся). Swimm.API тесты не
    /// собирают; его <c>CachedJson</c> кладёт один тип — готовый JSON со строковым ETag.
    /// </summary>
    [Fact]
    public void EveryCachedValueType_SurvivesRedis()
    {
        var cached = CacheCalls()
            .Select(c => c.Called.GetGenericArguments()[0])
            .Where(t => !t.ContainsGenericParameters)
            .ToHashSet();

        // Сторож самого теста: сканер, который тихо перестал находить вызовы, зеленеет вечно.
        // Проверено и в обратную сторону: на коде до К2 (кортеж и сущности Record в кэше) тест падает.
        Assert.True(cached.Count >= 15,
            $"Найдено всего {cached.Count} типов в вызовах кэша — сканер IL сломался?");
        Assert.Contains(typeof(IReadOnlyList<CategoryDto>), cached); // CategoryRepository, асинхронный метод

        var offenders = cached
            .Select(t => CacheValueRules.Violation(t))
            .Where(v => v is not null)
            .ToList();
        Assert.True(offenders.Count == 0,
            "В кэш кладутся типы, которые не переживут Redis: " + string.Join("; ", offenders));
    }

    /// <summary>
    /// Запись в кэш — только через <c>GetOrCreateAsync</c> (К3, docs/plans/cache-tags-plan.md §4-7):
    /// у такой записи своя сборка, и метки своих таблиц она получает сама. Ручной <c>SetAsync</c>
    /// вне чужой сборки кладёт запись без меток таблиц — её снимет только общий сброс, а когда
    /// записи перестанут звать общий сброс (К4), она будет врать до конца TTL.
    /// </summary>
    [Fact]
    public void NoManualSetAsync_OutsideTheCacheItself()
    {
        var offenders = CacheCalls()
            .Where(c => c.Called.Name == "SetAsync")
            .Select(c => OuterType(c.Caller.DeclaringType!))
            .Where(t => t != typeof(MemoryCacheService) && t != typeof(ICacheService))
            .Select(t => t.Name)
            .Distinct()
            .ToList();

        Assert.True(offenders.Count == 0,
            "SetAsync вызывают напрямую (нужен GetOrCreateAsync): " + string.Join(", ", offenders));
    }

    /// <summary>Внешний тип для сгенерированных компилятором (асинхронные методы, лямбды).</summary>
    private static Type OuterType(Type type)
    {
        while (type.DeclaringType is not null) type = type.DeclaringType;
        return type;
    }

    /// <summary>Все вызовы обобщённых методов <see cref="ICacheService"/> в Infrastructure и Application.</summary>
    private static List<(MethodBase Caller, MethodInfo Called)> CacheCalls()
    {
        var calls = new List<(MethodBase, MethodInfo)>();
        foreach (var assembly in new[] { typeof(ResultRepository).Assembly, typeof(ResultDto).Assembly })
            foreach (var type in assembly.GetTypes())
                foreach (var method in type.GetMethods(AllDeclared).Cast<MethodBase>()
                             .Concat(type.GetConstructors(AllDeclared)))
                    CollectCacheCalls(method, calls);
        return calls;
    }

    private const BindingFlags AllDeclared =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static
        | BindingFlags.DeclaredOnly;

    private static readonly string[] CacheMethods = ["GetAsync", "SetAsync", "GetOrCreateAsync"];

    private static readonly Dictionary<short, OpCode> OpCodesByValue = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(f => (OpCode)f.GetValue(null)!)
        .ToDictionary(o => o.Value);

    private static void CollectCacheCalls(MethodBase method, List<(MethodBase, MethodInfo)> into)
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
                    if (method.Module.ResolveMethod(token, typeArgs, methodArgs) is MethodInfo
                        {
                            IsGenericMethod: true,
                        } called
                        && called.DeclaringType == typeof(ICacheService)
                        && CacheMethods.Contains(called.Name))
                    {
                        into.Add((method, called));
                    }
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
