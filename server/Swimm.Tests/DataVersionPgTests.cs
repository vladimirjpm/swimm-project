using Npgsql;
using Swimm.Infrastructure.Migrations;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Версия данных для синхронизации «прод → локально» (миграция DataVersionForSync, решение В17).
/// Главный инвариант: триггер версии стоит ровно на таблицах из <c>server/db/seed-tables.txt</c>.
/// Таблица в списке без триггера — её правка на проде не двинет версию, и локальная копия будет
/// «свежей», отставая; триггер вне списка — синхронизация не перенесёт то, что двигает версию.
/// Плюс 02-grants.sql выдаёт swimm_sync чтение именно по триггеру.
/// Против живого Postgres; пропуск, если он недоступен. Пишет только внутри откатываемой транзакции.
/// </summary>
public class DataVersionPgTests
{
    private const string Owner =
        "Host=localhost;Port=5445;Database=swimm;Username=swimm;Password=swimm_local_dev";
    private const string Runtime =
        "Host=localhost;Port=5445;Database=swimm;Username=swimm_rw;Password=swimm_rw_local_dev";

    private static List<string> SeedTables() =>
        File.ReadAllLines(Path.Combine(HostingModeTests.RepoRoot(), "server", "db", "seed-tables.txt"))
            .Select(l => (l.Contains('#') ? l[..l.IndexOf('#')] : l).Trim())
            .Where(l => l.Length > 0)
            .Order(StringComparer.Ordinal)
            .ToList();

    private static async Task<NpgsqlConnection?> TryOpen(string cs)
    {
        var conn = new NpgsqlConnection(cs);
        try { await conn.OpenAsync(); return conn; }
        catch { await conn.DisposeAsync(); return null; }
    }

    [Fact]
    public void MigrationSnapshot_MatchesSeedTables()
    {
        // Без базы: новая таблица в seed-tables.txt требует новой миграции с её триггером.
        Assert.Equal(SeedTables(), DataVersionForSync.Tables.Order(StringComparer.Ordinal).ToList());
    }

    [Fact]
    public async Task Trigger_IsExactlyOnSeedTables_WithVersionRows()
    {
        await using var conn = await TryOpen(Owner);
        if (conn is null) return; // PG недоступен — пропуск

        var withTrigger = new List<string>();
        await using (var cmd = new NpgsqlCommand("""
            SELECT c.relname FROM pg_trigger t JOIN pg_class c ON c.oid = t.tgrelid
             WHERE t.tgname = 'Sys_DataVersion_Bump' ORDER BY 1
            """, conn))
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync()) withTrigger.Add(r.GetString(0));

        var rows = new List<string>();
        await using (var cmd = new NpgsqlCommand("""SELECT "Table" FROM "Sys_DataVersion" ORDER BY 1""", conn))
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync()) rows.Add(r.GetString(0));

        var seed = SeedTables();
        Assert.Equal(seed, withTrigger.Order(StringComparer.Ordinal).ToList());
        Assert.Equal(seed, rows.Order(StringComparer.Ordinal).ToList());
    }

    [Fact]
    public async Task Write_ByRuntimeRole_BumpsVersion_EvenWhenNoRowsMatch()
    {
        // swimm_rw прав на служебную таблицу не имеет — пишет функция SECURITY DEFINER. Триггер
        // statement-level: срабатывает и на UPDATE без строк, раз на оператор.
        await using var conn = await TryOpen(Runtime);
        if (conn is null) return;
        await using var tx = await conn.BeginTransactionAsync();

        async Task<long> TableVersion()
        {
            await using var cmd = new NpgsqlCommand("""SELECT "Version" FROM "Sys_DataVersion" WHERE "Table" = 'Styles'""", conn, tx);
            return (long)(await cmd.ExecuteScalarAsync())!;
        }

        var before = await TableVersion();
        await using (var cmd = new NpgsqlCommand("""UPDATE "Styles" SET "Id" = "Id" WHERE false""", conn, tx))
            await cmd.ExecuteNonQueryAsync();
        var after = await TableVersion();

        await using (var cmd = new NpgsqlCommand(DataSyncStatusService.ProdVersionSql, conn, tx))
        {
            // Читает ли ту же версию код, что сверяет отставание.
            var seq = Convert.ToInt64(await cmd.ExecuteScalarAsync());
            Assert.Equal(after, seq);
        }

        Assert.True(after > before);
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task LockedVersionRow_DoesNotBlockOtherWriters()
    {
        // Длинный импорт держит строку своей таблицы до коммита. Второй писатель той же таблицы
        // не должен её ждать (SKIP LOCKED): версия у него всё равно растёт, подпись — пропускается.
        await using var a = await TryOpen(Runtime);
        if (a is null) return;
        await using var b = await TryOpen(Runtime);
        if (b is null) return;

        await using var txA = await a.BeginTransactionAsync();
        await using (var cmd = new NpgsqlCommand("""UPDATE "Styles" SET "Id" = "Id" WHERE false""", a, txA))
            await cmd.ExecuteNonQueryAsync();

        await using var txB = await b.BeginTransactionAsync();
        await using (var cmd = new NpgsqlCommand("SET LOCAL lock_timeout = '2s'", b, txB))
            await cmd.ExecuteNonQueryAsync();
        await using (var cmd = new NpgsqlCommand("""UPDATE "Styles" SET "Id" = "Id" WHERE false""", b, txB))
            await cmd.ExecuteNonQueryAsync(); // упало бы по lock_timeout, если бы ждало

        await txB.RollbackAsync();
        await txA.RollbackAsync();
    }
}
