using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Swimm.API.Data;

namespace Swimm.API.Services;

/// <summary>
/// Предоставляет полную информацию о структуре БД (таблицы, FK, индексы, CHECK, функции, Views, row counts).
/// Результат кешируется с длительностью из AdminSettingsService.
///
/// Запросы используют системные каталоги PostgreSQL (information_schema + pg_catalog) —
/// это единственное место в проекте с вендор-специфичным SQL. Оно изолировано и обслуживает
/// только админ-вьювер схемы БД (страница db.html); на бизнес-логику не влияет.
/// </summary>
public class DbSchemaService
{
    private readonly SwimmDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly AdminSettingsService _settings;

    private const string CacheKey = "DbSchema";

    public DbSchemaService(SwimmDbContext db, IMemoryCache cache, AdminSettingsService settings)
    {
        _db = db;
        _cache = cache;
        _settings = settings;
    }

    public async Task<object> GetSchemaAsync(bool forceRefresh = false)
    {
        // Настройка ForceRefresh — если true, кеш обновляется при каждом запросе
        var alwaysRefresh = _settings.GetValue("ForceRefresh", false);
        if (forceRefresh || alwaysRefresh)
            _cache.Remove(CacheKey);

        // Настройка SchemaCacheTTL — время жизни кеша в минутах
        var ttlMinutes = _settings.GetValue("SchemaCacheTTL", 10);

        return (await _cache.GetOrCreateAsync(CacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(ttlMinutes);
            return await LoadSchemaAsync();
        }))!;
    }

    private async Task<object> LoadSchemaAsync()
    {
        var conn = _db.Database.GetDbConnection();
        await conn.OpenAsync();
        var dbName = conn.Database;

        // Настройка DefaultSchema — имя SQL-схемы по умолчанию (в PostgreSQL обычно "public")
        var schema = _settings.GetValue("DefaultSchema", "public");
        // Настройка ShowSystemTables — показывать ли системные объекты (pg_catalog/information_schema)
        var showSystem = _settings.GetValue("ShowSystemTables", false);

        var tables = await LoadTablesAsync(conn, schema, showSystem);
        var foreignKeys = await LoadForeignKeysAsync(conn, schema, showSystem);
        var indexes = await LoadIndexesAsync(conn, schema, showSystem);
        var checks = await LoadCheckConstraintsAsync(conn, schema, showSystem);
        var procedures = await LoadFunctionsAsync(conn, schema, showSystem);
        var views = await LoadViewsAsync(conn, schema, showSystem);
        var rowCounts = await LoadRowCountsAsync(conn, schema, showSystem);

        return new
        {
            database = dbName,
            tables,
            foreignKeys,
            indexes,
            checkConstraints = checks,
            storedProcedures = procedures,
            views,
            rowCounts
        };
    }

    /// <summary>
    /// Предикат фильтрации по схеме. Если ShowSystemTables = true — отдаём всё (включая
    /// системные схемы), иначе ограничиваемся выбранной пользовательской схемой.
    /// </summary>
    private static string SchemaPredicate(string column, string schema, bool showSystem)
        => showSystem ? "TRUE" : $"{column} = '{schema}'";

    private static async Task<List<object>> LoadTablesAsync(System.Data.Common.DbConnection conn, string schema, bool showSystem)
    {
        var tables = new List<object>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT
                c.table_name,
                c.column_name,
                c.ordinal_position::int        AS ordinal_position,
                c.data_type,
                c.character_maximum_length::int AS character_maximum_length,
                c.is_nullable,
                c.column_default,
                CASE WHEN pk.column_name IS NOT NULL THEN 1 ELSE 0 END AS is_pk,
                CASE WHEN c.is_identity = 'YES' OR c.column_default LIKE 'nextval(%' THEN 1 ELSE 0 END AS is_identity
            FROM information_schema.tables t
            JOIN information_schema.columns c
                ON c.table_name = t.table_name AND c.table_schema = t.table_schema
            LEFT JOIN (
                SELECT ku.table_name, ku.column_name, ku.table_schema
                FROM information_schema.table_constraints tc
                JOIN information_schema.key_column_usage ku
                    ON tc.constraint_name = ku.constraint_name
                    AND tc.table_schema = ku.table_schema
                WHERE tc.constraint_type = 'PRIMARY KEY'
            ) pk ON pk.table_name = c.table_name AND pk.column_name = c.column_name AND pk.table_schema = c.table_schema
            WHERE t.table_type = 'BASE TABLE' AND {SchemaPredicate("t.table_schema", schema, showSystem)}
            ORDER BY c.table_name, c.ordinal_position
            """;
        using var reader = await cmd.ExecuteReaderAsync();
        var grouped = new Dictionary<string, List<object>>();
        while (await reader.ReadAsync())
        {
            var tbl = reader.GetString(0);
            if (!grouped.ContainsKey(tbl)) grouped[tbl] = new List<object>();
            grouped[tbl].Add(new
            {
                column = reader.GetString(1),
                position = reader.GetInt32(2),
                dataType = reader.GetString(3),
                maxLength = reader.IsDBNull(4) ? (int?)null : reader.GetInt32(4),
                nullable = reader.GetString(5) == "YES",
                defaultValue = reader.IsDBNull(6) ? null : reader.GetString(6),
                isPk = reader.GetInt32(7) == 1,
                isIdentity = reader.GetInt32(8) == 1
            });
        }
        foreach (var kv in grouped)
            tables.Add(new { name = kv.Key, columns = kv.Value });
        return tables;
    }

    private static async Task<List<object>> LoadForeignKeysAsync(System.Data.Common.DbConnection conn, string schema, bool showSystem)
    {
        var list = new List<object>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT
                tc.constraint_name AS fk_name,
                tc.table_name      AS parent_table,
                kcu.column_name    AS parent_column,
                ccu.table_name     AS referenced_table,
                ccu.column_name    AS referenced_column,
                rc.delete_rule     AS on_delete
            FROM information_schema.table_constraints tc
            JOIN information_schema.key_column_usage kcu
                ON tc.constraint_name = kcu.constraint_name AND tc.table_schema = kcu.table_schema
            JOIN information_schema.constraint_column_usage ccu
                ON tc.constraint_name = ccu.constraint_name AND tc.table_schema = ccu.table_schema
            JOIN information_schema.referential_constraints rc
                ON tc.constraint_name = rc.constraint_name AND tc.table_schema = rc.constraint_schema
            WHERE tc.constraint_type = 'FOREIGN KEY' AND {SchemaPredicate("tc.table_schema", schema, showSystem)}
            ORDER BY tc.table_name, tc.constraint_name
            """;
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new
            {
                name = reader.GetString(0),
                table = reader.GetString(1),
                column = reader.GetString(2),
                referencedTable = reader.GetString(3),
                referencedColumn = reader.GetString(4),
                onDelete = reader.GetString(5)
            });
        }
        return list;
    }

    private static async Task<List<object>> LoadIndexesAsync(System.Data.Common.DbConnection conn, string schema, bool showSystem)
    {
        var list = new List<object>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT
                t.relname  AS table_name,
                i.relname  AS index_name,
                am.amname  AS index_type,
                ix.indisunique AS is_unique,
                string_agg(a.attname, ', ' ORDER BY k.ord) AS columns
            FROM pg_index ix
            JOIN pg_class i ON i.oid = ix.indexrelid
            JOIN pg_class t ON t.oid = ix.indrelid
            JOIN pg_am am ON am.oid = i.relam
            JOIN pg_namespace n ON n.oid = t.relnamespace
            JOIN LATERAL unnest(ix.indkey) WITH ORDINALITY AS k(attnum, ord) ON TRUE
            JOIN pg_attribute a ON a.attrelid = t.oid AND a.attnum = k.attnum
            WHERE {SchemaPredicate("n.nspname", schema, showSystem)}
            GROUP BY t.relname, i.relname, am.amname, ix.indisunique
            ORDER BY t.relname, i.relname
            """;
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new
            {
                table = reader.GetString(0),
                name = reader.GetString(1),
                type = reader.GetString(2),
                isUnique = reader.GetBoolean(3),
                columns = reader.IsDBNull(4) ? "" : reader.GetString(4)
            });
        }
        return list;
    }

    private static async Task<List<object>> LoadCheckConstraintsAsync(System.Data.Common.DbConnection conn, string schema, bool showSystem)
    {
        var list = new List<object>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT
                t.relname AS table_name,
                con.conname AS constraint_name,
                pg_get_constraintdef(con.oid) AS definition
            FROM pg_constraint con
            JOIN pg_class t ON t.oid = con.conrelid
            JOIN pg_namespace n ON n.oid = t.relnamespace
            WHERE con.contype = 'c' AND {SchemaPredicate("n.nspname", schema, showSystem)}
            ORDER BY t.relname, con.conname
            """;
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new
            {
                table = reader.GetString(0),
                name = reader.GetString(1),
                definition = reader.GetString(2)
            });
        }
        return list;
    }

    private static async Task<List<object>> LoadFunctionsAsync(System.Data.Common.DbConnection conn, string schema, bool showSystem)
    {
        var list = new List<object>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT
                p.proname AS name,
                pg_get_functiondef(p.oid) AS definition
            FROM pg_proc p
            JOIN pg_namespace n ON n.oid = p.pronamespace
            WHERE {SchemaPredicate("n.nspname", schema, showSystem)}
            ORDER BY p.proname
            """;
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new
            {
                name = reader.GetString(0),
                createdAt = (DateTime?)null,
                modifiedAt = (DateTime?)null,
                definition = reader.IsDBNull(1) ? null : reader.GetString(1)
            });
        }
        return list;
    }

    private static async Task<List<object>> LoadViewsAsync(System.Data.Common.DbConnection conn, string schema, bool showSystem)
    {
        var list = new List<object>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT
                table_name AS name,
                view_definition AS definition
            FROM information_schema.views
            WHERE {SchemaPredicate("table_schema", schema, showSystem)}
            ORDER BY table_name
            """;
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new
            {
                name = reader.GetString(0),
                createdAt = (DateTime?)null,
                modifiedAt = (DateTime?)null,
                definition = reader.IsDBNull(1) ? null : reader.GetString(1)
            });
        }
        return list;
    }

    private static async Task<List<object>> LoadRowCountsAsync(System.Data.Common.DbConnection conn, string schema, bool showSystem)
    {
        var list = new List<object>();
        using var cmd = conn.CreateCommand();
        // n_live_tup — поддерживаемая в реальном времени оценка числа живых строк.
        cmd.CommandText = $"""
            SELECT
                relname,
                n_live_tup
            FROM pg_stat_user_tables
            WHERE {SchemaPredicate("schemaname", schema, showSystem)}
            ORDER BY relname
            """;
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new
            {
                table = reader.GetString(0),
                rows = reader.GetInt64(1)
            });
        }
        return list;
    }
}
