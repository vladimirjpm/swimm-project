using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Swimm.Infrastructure.Data;

namespace Swimm.API;

/// <summary>
/// Design-time фабрика для EF-инструментов (`dotnet ef migrations` / `database update`).
/// Подключается под привилегированной owner-ролью (MigrationConnection), т.к. инструментам
/// нужен DDL. Рантайм-регистрация контекста использует ограниченную swimm_rw-роль —
/// эта фабрика применяется ТОЛЬКО инструментами и не влияет на приложение.
///
/// Также снимает неоднозначность «найдено более одного DbContext» (есть ещё
/// <see cref="SwimmReadDbContext"/>): инструменты используют контекст с фабрикой.
///
/// Лежит в startup-проекте (Swimm.API), где доступна полная конфигурация и appsettings.
/// </summary>
public class SwimmDbContextFactory : IDesignTimeDbContextFactory<SwimmDbContext>
{
    public SwimmDbContext CreateDbContext(string[] args)
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = config.GetConnectionString("MigrationConnection")
            ?? config.GetConnectionString("DefaultConnection");

        var options = new DbContextOptionsBuilder<SwimmDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new SwimmDbContext(options);
    }
}
