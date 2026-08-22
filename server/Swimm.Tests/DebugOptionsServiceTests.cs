using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Отладочные подробности — ДВА уровня: общий тумблер `DebugDetails` в /Admin/Settings и
/// галочка конкретной опции в `Sys_DebugOptions`. Тесты охраняют главное свойство: пока
/// общий выключен, ни одна опция не действует, сколько бы галочек ни стояло.
/// </summary>
public class DebugOptionsServiceTests
{
    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private sealed class SettingsStub(bool master) : ISettingsService
    {
        public IReadOnlyList<AdminSetting> GetAll() => [];
        public AdminSetting? Get(string key) => null;
        public T GetValue<T>(string key, T fallback) =>
            key == "DebugDetails" && fallback is bool ? (T)(object)master : fallback;
        public bool Update(string key, string newValue) => true;
    }

    [Fact]
    public async Task GetAll_SeedsKnownOptions_DisabledByDefault()
    {
        using var db = CreateDb(nameof(GetAll_SeedsKnownOptions_DisabledByDefault));
        var service = new DebugOptionsService(db, new SettingsStub(master: true));

        var state = await service.GetAllAsync();

        // Новая подробность появляется в коде (DebugOptionKeys.All), а не миграцией на каждую.
        var option = Assert.Single(state.Options, o => o.Key == DebugOptionKeys.ShowAgeRecordsDetails);
        Assert.False(option.Enabled);
        Assert.False(option.Effective);
        Assert.NotEmpty(option.Title);
    }

    [Fact]
    public async Task MasterOff_KeepsEveryOptionSilent()
    {
        using var db = CreateDb(nameof(MasterOff_KeepsEveryOptionSilent));

        // Галочка стоит…
        var on = new DebugOptionsService(db, new SettingsStub(master: true));
        await on.SetAsync(DebugOptionKeys.ShowAgeRecordsDetails, true, "vlad");
        Assert.True(await on.IsEnabledAsync(DebugOptionKeys.ShowAgeRecordsDetails));

        // …но общий тумблер выключили — и подробность молчит, галочку трогать не нужно.
        var off = new DebugOptionsService(db, new SettingsStub(master: false));
        Assert.False(await off.IsEnabledAsync(DebugOptionKeys.ShowAgeRecordsDetails));

        var state = await off.GetAllAsync();
        var option = Assert.Single(state.Options, o => o.Key == DebugOptionKeys.ShowAgeRecordsDetails);
        Assert.True(option.Enabled);       // галочка на месте
        Assert.False(option.Effective);    // но на деле не действует
        Assert.False(state.MasterEnabled);
    }

    [Fact]
    public async Task Set_StoresWhoAndWhen_AndUnknownKeyIsRejected()
    {
        using var db = CreateDb(nameof(Set_StoresWhoAndWhen_AndUnknownKeyIsRejected));
        var service = new DebugOptionsService(db, new SettingsStub(master: true));

        Assert.True(await service.SetAsync(DebugOptionKeys.ShowAgeRecordsDetails, true, "vlad"));
        Assert.False(await service.SetAsync("NoSuchOption", true, "vlad"));

        var row = await db.DebugOptions.SingleAsync(o => o.Key == DebugOptionKeys.ShowAgeRecordsDetails);
        Assert.True(row.Enabled);
        Assert.Equal("vlad", row.UpdatedBy);
        Assert.NotEqual(default, row.UpdatedAt);
    }

    [Fact]
    public async Task UnknownKey_IsNeverEnabled()
    {
        using var db = CreateDb(nameof(UnknownKey_IsNeverEnabled));
        var service = new DebugOptionsService(db, new SettingsStub(master: true));

        Assert.False(await service.IsEnabledAsync("NoSuchOption"));
    }
}
