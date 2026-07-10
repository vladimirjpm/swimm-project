using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

public class AdminSettingsServiceTests
{
    // Реальный MemoryCache — AdminSettingsService использует его только для инвалидации
    // кеша схемы при Update(); нам достаточно живого экземпляра без Assert-ов по кешу.
    private static AdminSettingsService Build() =>
        new(new MemoryCache(Options.Create(new MemoryCacheOptions())));

    // ── GetAll: все дефолты при инициализации ───────────────────────────────

    [Fact]
    public void GetAll_OnInit_ContainsAllDefaults()
    {
        var svc = Build();

        var all = svc.GetAll();

        // 6 базовых + 3 настройки HubGroups (Policy/MaxPerUser/Visibility, фаза 8.1).
        Assert.Equal(9, all.Count);
    }

    // ── Get: существующий ключ возвращает запись ─────────────────────────────

    [Theory]
    [InlineData("MaintenanceMode")]
    [InlineData("SchemaCacheTTL")]
    [InlineData("ForceRefresh")]
    [InlineData("ShowSystemTables")]
    [InlineData("DefaultSchema")]
    [InlineData("ResultsLoadMode")]
    [InlineData("HubGroupCreationPolicy")]
    [InlineData("HubGroupMaxPerUser")]
    [InlineData("HubGroupVisibility")]
    public void Get_ExistingKey_ReturnsNonNull(string key)
    {
        var svc = Build();

        Assert.NotNull(svc.Get(key));
    }

    // ── Get: несуществующий ключ возвращает null ─────────────────────────────

    [Fact]
    public void Get_MissingKey_ReturnsNull()
    {
        var svc = Build();

        Assert.Null(svc.Get("NonExistentKey"));
    }

    // ── GetValue: корректный int парсится ────────────────────────────────────

    [Fact]
    public void GetValue_ValidIntKey_ReturnsParsedValue()
    {
        var svc = Build();

        var value = svc.GetValue<int>("SchemaCacheTTL", -1);

        Assert.Equal(10, value);
    }

    // ── GetValue: отсутствующий ключ — возвращается fallback ─────────────────

    [Fact]
    public void GetValue_MissingKey_ReturnsFallback()
    {
        var svc = Build();

        var value = svc.GetValue<int>("NonExistentKey", 42);

        Assert.Equal(42, value);
    }

    // ── Update: валидное bool-значение — true ────────────────────────────────

    [Fact]
    public void Update_ValidBoolValue_ReturnsTrueAndPersists()
    {
        var svc = Build();

        var ok = svc.Update("MaintenanceMode", "true");

        Assert.True(ok);
        Assert.Equal("true", svc.Get("MaintenanceMode")!.Value);
    }

    // ── Update: некорректный тип — false, значение не меняется ───────────────

    [Fact]
    public void Update_InvalidBoolValue_ReturnsFalse()
    {
        var svc = Build();

        var ok = svc.Update("MaintenanceMode", "notabool");

        Assert.False(ok);
        Assert.Equal("false", svc.Get("MaintenanceMode")!.Value);
    }

    // ── Update: несуществующий ключ — false ──────────────────────────────────

    [Fact]
    public void Update_MissingKey_ReturnsFalse()
    {
        var svc = Build();

        var ok = svc.Update("GhostKey", "value");

        Assert.False(ok);
    }

    // ── Update: ResultsLoadMode — только full/paged/client ────────────────────

    [Theory]
    [InlineData("full")]
    [InlineData("paged")]
    [InlineData("client")]
    public void Update_ResultsLoadMode_ValidValue_ReturnsTrue(string value)
    {
        var svc = Build();

        var ok = svc.Update("ResultsLoadMode", value);

        Assert.True(ok);
        Assert.Equal(value, svc.Get("ResultsLoadMode")!.Value);
    }

    [Theory]
    [InlineData("FULL")] // регистр важен — клиент сравнивает строго
    [InlineData("pagedd")]
    [InlineData("")]
    public void Update_ResultsLoadMode_InvalidValue_ReturnsFalse(string value)
    {
        var svc = Build();

        var ok = svc.Update("ResultsLoadMode", value);

        Assert.False(ok);
        Assert.Equal("client", svc.Get("ResultsLoadMode")!.Value);
    }
}
