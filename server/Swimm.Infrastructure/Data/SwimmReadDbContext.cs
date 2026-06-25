using Microsoft.EntityFrameworkCore;

namespace Swimm.Infrastructure.Data;

/// <summary>
/// Read-only вариант <see cref="SwimmDbContext"/> для публичного read-пути.
/// Делит ту же модель (наследует OnModelCreating), но подключается под SELECT-only
/// DB-ролью (swimm_ro) через отдельную строку подключения (ReadConnection).
///
/// Изоляция обеспечивается на уровне БД (GRANT'ы роли), а не только типом контекста:
/// даже вызов SaveChanges здесь будет отклонён сервером. Тип нужен, чтобы read-репозитории
/// физически не могли получить привилегированное соединение через DI.
///
/// Миграции к этому контексту НЕ применяются — снапшот и миграции принадлежат только
/// <see cref="SwimmDbContext"/> (при `dotnet ef` указывайте `--context SwimmDbContext`).
/// </summary>
public class SwimmReadDbContext : SwimmDbContext
{
    public SwimmReadDbContext(DbContextOptions<SwimmReadDbContext> options) : base(options)
    {
    }
}
