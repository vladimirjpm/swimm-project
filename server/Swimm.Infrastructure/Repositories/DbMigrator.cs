using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Repositories;

public class DbMigrator : IDbMigrator
{
    private readonly SwimmDbContext _db;

    public DbMigrator(SwimmDbContext db)
    {
        _db = db;
    }

    public void Migrate() => _db.Database.Migrate();
}
