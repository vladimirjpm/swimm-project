using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Swimm.Infrastructure;
using Swimm.Infrastructure.Data;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Тёплый пул (NpgsqlPoolDefaults, решение Влада 19.11.2026): минимум соединений и keepalive там, где
/// строка подключения их не задала; заданное явно — не трогаем. Плюс проверка, что оба контекста EF
/// реально получают эти настройки через AddInfrastructure.
/// </summary>
public class NpgsqlPoolDefaultsTests
{
    private const string Base = "Host=localhost;Port=5445;Database=swimm;Username=swimm;Password=x";

    private static NpgsqlConnectionStringBuilder Applied(string connectionString)
    {
        var ds = new NpgsqlDataSourceBuilder(connectionString);
        NpgsqlPoolDefaults.Apply(ds);
        return ds.ConnectionStringBuilder;
    }

    [Fact]
    public void Defaults_SetMinPoolAndKeepAlive()
    {
        var cs = Applied(Base + ";Maximum Pool Size=10");
        Assert.Equal(2, cs.MinPoolSize);
        Assert.Equal(30, cs.KeepAlive);
        Assert.Equal(10, cs.MaxPoolSize);
    }

    [Fact]
    public void ExplicitValues_AreKept()
    {
        var cs = Applied(Base + ";Minimum Pool Size=5;Keepalive=120");
        Assert.Equal(5, cs.MinPoolSize);
        Assert.Equal(120, cs.KeepAlive);
    }

    [Fact]
    public void MinPool_NeverAboveMaxPool()
    {
        var cs = Applied(Base + ";Maximum Pool Size=1");
        Assert.Equal(1, cs.MinPoolSize);
    }

    [Fact]
    public void PoolingOff_Untouched()
    {
        var cs = Applied(Base + ";Pooling=false");
        Assert.Equal(0, cs.MinPoolSize);
        Assert.Equal(0, cs.KeepAlive);
    }

    [Fact]
    public void BothEfContexts_GetWarmPool()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = Base + ";Maximum Pool Size=10",
            ["ConnectionStrings:AdminConnection"] = Base + ";Maximum Pool Size=10",
            ["ConnectionStrings:ReadConnection"] = Base + ";Maximum Pool Size=20",
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryCache();
        services.AddInfrastructure(config);
        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        foreach (var db in new DbContext[]
                 {
                     scope.ServiceProvider.GetRequiredService<SwimmDbContext>(),
                     scope.ServiceProvider.GetRequiredService<SwimmReadDbContext>(),
                 })
        {
            // Строка соединения EF — уже из собранного источника (с нашими настройками).
            var cs = new NpgsqlConnectionStringBuilder(db.Database.GetDbConnection().ConnectionString);
            Assert.Equal(2, cs.MinPoolSize);
            Assert.Equal(30, cs.KeepAlive);
        }
    }
}
