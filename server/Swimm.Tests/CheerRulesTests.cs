using System.Reflection;
using Microsoft.AspNetCore.RateLimiting;
using Swimm.API.Controllers;
using Swimm.Application.Mapping;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Лимиты 🎉 (решение Влада 25.10.2026): клики — под общей политикой реакций, чтение счётчиков —
/// под своей мягкой политикой по IP. Интеграционного хоста в тестах нет, поэтому держим то, что
/// легко потерять: какой атрибут стоит на каком методе и хватает ли бюджета обычной странице.
/// </summary>
public class CheerRulesTests
{
    private static MethodInfo Method(string name) =>
        typeof(ReactionsController).GetMethod(name, BindingFlags.Public | BindingFlags.Instance)!;

    [Fact]
    public void Read_HasOwnIpPolicy_NotReactionsAndNotUnlimited()
    {
        var read = Method(nameof(ReactionsController.GetCheers));

        var attr = Assert.Single(read.GetCustomAttributes<EnableRateLimitingAttribute>());
        Assert.Equal(CheerRules.ReadRateLimitPolicy, attr.PolicyName);
        Assert.Empty(read.GetCustomAttributes<DisableRateLimitingAttribute>());
    }

    [Fact]
    public void Clicks_StayUnderReactionsPolicy()
    {
        // Политика кликов — атрибут класса; своего у методов быть не должно, иначе он её подменит.
        var cls = Assert.Single(typeof(ReactionsController).GetCustomAttributes<EnableRateLimitingAttribute>());
        Assert.Equal("reactions", cls.PolicyName);
        Assert.Empty(Method(nameof(ReactionsController.Cheer)).GetCustomAttributes<EnableRateLimitingAttribute>());
        Assert.Empty(Method(nameof(ReactionsController.Uncheer)).GetCustomAttributes<EnableRateLimitingAttribute>());
    }

    [Fact]
    public void Budget_FitsTenLoadsOfTheLargestProtocolPerMinute()
    {
        // Пачка клиента обязана помещаться в предел сервера, иначе каждый запрос — 400.
        Assert.True(CheerRules.ClientChunk <= CheerRules.MaxIdsPerRead);

        var requestsPerLoad = (int)Math.Ceiling(CheerRules.LargestProtocolRows / (double)CheerRules.ClientChunk);
        // Десять полных загрузок самого большого протокола в минуту с одного IP — обычная
        // страница (и несколько человек за одним Wi-Fi) в лимит не упирается.
        Assert.True(requestsPerLoad * 10 <= CheerRules.ReadPerMinute,
            $"{requestsPerLoad} запросов на загрузку × 10 > {CheerRules.ReadPerMinute}");
    }
}
