using Microsoft.Extensions.Caching.Memory;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Настройки в БД (решение В7, docs/plans/azure-deploy-plan.md §10): реестр и дефолты в коде,
/// изменённые значения — в хранилище. С В17 правку делает ДРУГОЙ процесс (локальная админка в
/// ProdAdmin), поэтому важно: правка доходит до хранилища, перечитывание её подхватывает, а
/// сбой базы не превращается ни в «изменилось только тут», ни в падение сайта.
/// Настоящая таблица — <c>AdminSettingsStorePgTests</c>.
/// </summary>
public class AdminSettingsPersistenceTests
{
    private sealed class FakeStore : IAdminSettingsStore
    {
        public readonly Dictionary<string, string> Rows = new(StringComparer.Ordinal);
        public int Loads;
        public bool FailLoad;
        public bool FailSave;

        public IReadOnlyDictionary<string, string> LoadAll()
        {
            Loads++;
            if (FailLoad) throw new InvalidOperationException("база лежит");
            return new Dictionary<string, string>(Rows);
        }

        public void Save(string key, string value)
        {
            if (FailSave) throw new InvalidOperationException("база лежит");
            Rows[key] = value;
        }
    }

    private static AdminSettingsService Build(FakeStore store) =>
        new(new MemoryCache(new MemoryCacheOptions()), store: store);

    [Fact]
    public void Update_WritesToStore()
    {
        var store = new FakeStore();
        var svc = Build(store);

        Assert.True(svc.Update("MaintenanceMode", "true"));

        Assert.Equal("true", store.Rows["MaintenanceMode"]);
        Assert.True(svc.GetValue("MaintenanceMode", false));
    }

    [Fact]
    public void StoredValue_WinsOverDefault_OnFirstRead()
    {
        // Режим обслуживания, включённый локальной админкой, прод видит при первом же чтении.
        var store = new FakeStore { Rows = { ["MaintenanceMode"] = "true" } };
        var svc = Build(store);

        Assert.True(svc.GetValue("MaintenanceMode", false));
    }

    [Fact]
    public void Reload_PicksUpChangeFromAnotherProcess_AndRemovedRowMeansDefault()
    {
        var store = new FakeStore();
        var svc = Build(store);
        Assert.False(svc.GetValue("MaintenanceMode", true));

        store.Rows["MaintenanceMode"] = "true";   // так пишет другой процесс
        svc.Reload();                              // так велит мост кэша
        Assert.True(svc.GetValue("MaintenanceMode", false));

        store.Rows.Remove("MaintenanceMode");
        svc.Reload();
        Assert.False(svc.GetValue("MaintenanceMode", true));
    }

    [Fact]
    public void InvalidStoredValue_IsIgnored_DefaultStays()
    {
        // Ручная правка таблицы с опечаткой не должна сломать клиент — правила те же, что у Update.
        var store = new FakeStore { Rows = { ["ResultsLoadMode"] = "fulll", ["HubGroupMaxPerUser"] = "abc" } };
        var svc = Build(store);

        Assert.Equal("client", svc.GetValue("ResultsLoadMode", "?"));
        Assert.Equal(3, svc.GetValue("HubGroupMaxPerUser", -1));
    }

    [Fact]
    public void UnknownStoredKey_IsIgnored()
    {
        var store = new FakeStore { Rows = { ["NoSuchSetting"] = "1" } };
        var svc = Build(store);

        Assert.Null(svc.Get("NoSuchSetting"));
        Assert.DoesNotContain(svc.GetAll(), s => s.Key == "NoSuchSetting");
    }

    [Fact]
    public void SaveFailure_LeavesValueUnchanged()
    {
        // Сначала база, потом память: иначе этот процесс и прод разошлись бы молча.
        var store = new FakeStore { FailSave = true };
        var svc = Build(store);

        Assert.False(svc.Update("MaintenanceMode", "true"));
        Assert.False(svc.GetValue("MaintenanceMode", true));
    }

    [Fact]
    public void LoadFailure_FallsBackToDefaults_AndDoesNotRetryOnEveryRead()
    {
        var store = new FakeStore { FailLoad = true };
        var svc = Build(store);

        Assert.Equal("client", svc.GetValue("ResultsLoadMode", "?"));
        Assert.Equal("client", svc.GetValue("ResultsLoadMode", "?"));
        Assert.Equal("client", svc.GetValue("ResultsLoadMode", "?"));

        Assert.Equal(1, store.Loads); // повтор — не раньше паузы, а не на каждом чтении
    }

    [Fact]
    public void InvalidUpdate_DoesNotReachStore()
    {
        var store = new FakeStore();
        var svc = Build(store);

        Assert.False(svc.Update("ResultsLoadMode", "nope"));
        Assert.Empty(store.Rows);
    }

    [Fact]
    public void LogligVerify_KeysAreInRegistry()
    {
        // Джоб ночной сверки читал их и раньше, но выключить из админки было нечем (§8.2).
        var svc = Build(new FakeStore());

        Assert.True(svc.GetValue("LogligVerifyEnabled", false));
        Assert.Equal(24, svc.GetValue("LogligVerifyIntervalHours", 0));
        Assert.True(svc.Update("LogligVerifyEnabled", "false"));
        Assert.False(svc.Update("LogligVerifyIntervalHours", "often"));
    }
}
