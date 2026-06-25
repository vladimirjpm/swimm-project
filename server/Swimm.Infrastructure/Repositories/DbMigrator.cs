using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Swimm.Application.Abstractions;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Repositories;

/// <summary>
/// Применяет миграции под привилегированной owner-ролью (DDL).
/// Строит собственный <see cref="SwimmDbContext"/> с MigrationConnection, не завися от
/// рантайм-регистрации контекста (которая использует ограниченную swimm_rw-роль без DDL).
/// </summary>
public class DbMigrator : IDbMigrator
{
    private readonly string? _migrationConnection;

    public DbMigrator(IConfiguration configuration)
    {
        _migrationConnection = configuration.GetConnectionString("MigrationConnection")
            ?? configuration.GetConnectionString("DefaultConnection");
    }

    public void Migrate()
    {
        var options = new DbContextOptionsBuilder<SwimmDbContext>()
            .UseNpgsql(_migrationConnection)
            .Options;

        using var db = new SwimmDbContext(options);
        db.Database.Migrate();
    }
}
