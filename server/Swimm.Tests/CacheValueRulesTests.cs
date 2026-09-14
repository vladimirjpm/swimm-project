using System.Reflection;
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
            .Select(c => IlCalls.OuterType(c.Caller.DeclaringType!))
            .Where(t => t != typeof(MemoryCacheService) && t != typeof(ICacheService))
            .Select(t => t.Name)
            .Distinct()
            .ToList();

        Assert.True(offenders.Count == 0,
            "SetAsync вызывают напрямую (нужен GetOrCreateAsync): " + string.Join(", ", offenders));
    }

    private static readonly string[] CacheMethods = ["GetAsync", "SetAsync", "GetOrCreateAsync"];

    /// <summary>Все вызовы обобщённых методов <see cref="ICacheService"/> в Infrastructure и Application.</summary>
    private static List<(MethodBase Caller, MethodInfo Called)> CacheCalls() => IlCalls.Find(
        [typeof(ResultRepository).Assembly, typeof(ResultDto).Assembly],
        called => called.IsGenericMethod
                  && called.DeclaringType == typeof(ICacheService)
                  && CacheMethods.Contains(called.Name));
}
