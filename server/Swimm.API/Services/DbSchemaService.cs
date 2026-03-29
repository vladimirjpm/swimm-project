using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Swimm.API.Data;

namespace Swimm.API.Services;

/// <summary>
/// ÐŸÑ€ÐµÐ´Ð¾ÑÑ‚Ð°Ð²Ð»ÑÐµÑ‚ Ð¿Ð¾Ð»Ð½ÑƒÑŽ Ð¸Ð½Ñ„Ð¾Ñ€Ð¼Ð°Ñ†Ð¸ÑŽ Ð¾ ÑÑ‚Ñ€ÑƒÐºÑ‚ÑƒÑ€Ðµ Ð‘Ð” (Ñ‚Ð°Ð±Ð»Ð¸Ñ†Ñ‹, FK, Ð¸Ð½Ð´ÐµÐºÑÑ‹, CHECK, SP, Views, row counts).
/// Ð ÐµÐ·ÑƒÐ»ÑŒÑ‚Ð°Ñ‚ ÐºÐµÑˆÐ¸Ñ€ÑƒÐµÑ‚ÑÑ Ñ Ð´Ð»Ð¸Ñ‚ÐµÐ»ÑŒÐ½Ð¾ÑÑ‚ÑŒÑŽ Ð¸Ð· AdminSettingsService.
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
        // ÐÐ°ÑÑ‚Ñ€Ð¾Ð¹ÐºÐ° ForceRefresh â€” ÐµÑÐ»Ð¸ true, ÐºÐµÑˆ Ð¾Ð±Ð½Ð¾Ð²Ð»ÑÐµÑ‚ÑÑ Ð¿Ñ€Ð¸ ÐºÐ°Ð¶Ð´Ð¾Ð¼ Ð·Ð°Ð¿Ñ€Ð¾ÑÐµ
        var alwaysRefresh = _settings.GetValue("ForceRefresh", false);
        if (forceRefresh || alwaysRefresh)
            _cache.Remove(CacheKey);

        // ÐÐ°ÑÑ‚Ñ€Ð¾Ð¹ÐºÐ° SchemaCacheTTL â€” Ð²Ñ€ÐµÐ¼Ñ Ð¶Ð¸Ð·Ð½Ð¸ ÐºÐµÑˆÐ° Ð² Ð¼Ð¸Ð½ÑƒÑ‚Ð°Ñ…
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

        // ÐÐ°ÑÑ‚Ñ€Ð¾Ð¹ÐºÐ° DefaultSchema â€” Ð¸Ð¼Ñ SQL-ÑÑ…ÐµÐ¼Ñ‹ Ð¿Ð¾ ÑƒÐ¼Ð¾Ð»Ñ‡Ð°Ð½Ð¸ÑŽ (dbo, staging Ð¸ Ñ‚. Ð´.)
        var schema = _settings.GetValue("DefaultSchema", "dbo");
        // ÐÐ°ÑÑ‚Ñ€Ð¾Ð¹ÐºÐ° ShowSystemTables â€” Ð¿Ð¾ÐºÐ°Ð·Ñ‹Ð²Ð°Ñ‚ÑŒ Ð»Ð¸ Ñ‚Ð°Ð±Ð»Ð¸Ñ†Ñ‹ Ñ is_ms_shipped = 1
        var showSystem = _settings.GetValue("ShowSystemTables", false);

        var tables = await LoadTablesAsync(conn, schema);
        var foreignKeys = await LoadForeignKeysAsync(conn, showSystem);
        var indexes = await LoadIndexesAsync(conn, showSystem);
        var checks = await LoadCheckConstraintsAsync(conn);
        var procedures = await LoadStoredProceduresAsync(conn);
        var views = await LoadViewsAsync(conn);
        var rowCounts = await LoadRowCountsAsync(conn, showSystem);

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

    private static async Task<List<object>> LoadTablesAsync(System.Data.Common.DbConnection conn, string schema)
    {
        var tables = new List<object>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT
                t.TABLE_NAME,
                c.COLUMN_NAME,
                c.ORDINAL_POSITION,
                c.DATA_TYPE,
                c.CHARACTER_MAXIMUM_LENGTH,
                c.IS_NULLABLE,
                c.COLUMN_DEFAULT,
                CASE WHEN pk.COLUMN_NAME IS NOT NULL THEN 1 ELSE 0 END AS IS_PK,
                COLUMNPROPERTY(OBJECT_ID(t.TABLE_SCHEMA + '.' + t.TABLE_NAME), c.COLUMN_NAME, 'IsIdentity') AS IS_IDENTITY
            FROM INFORMATION_SCHEMA.TABLES t
            JOIN INFORMATION_SCHEMA.COLUMNS c
                ON c.TABLE_NAME = t.TABLE_NAME AND c.TABLE_SCHEMA = t.TABLE_SCHEMA
            LEFT JOIN (
                SELECT ku.TABLE_NAME, ku.COLUMN_NAME, ku.TABLE_SCHEMA
                FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
                JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE ku
                    ON tc.CONSTRAINT_NAME = ku.CONSTRAINT_NAME
                    AND tc.TABLE_SCHEMA = ku.TABLE_SCHEMA
                WHERE tc.CONSTRAINT_TYPE = 'PRIMARY KEY'
            ) pk ON pk.TABLE_NAME = t.TABLE_NAME AND pk.COLUMN_NAME = c.COLUMN_NAME AND pk.TABLE_SCHEMA = t.TABLE_SCHEMA
            WHERE t.TABLE_TYPE = 'BASE TABLE' AND t.TABLE_SCHEMA = '{schema}'
            ORDER BY t.TABLE_NAME, c.ORDINAL_POSITION
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
                isIdentity = reader.IsDBNull(8) ? false : reader.GetInt32(8) == 1
            });
        }
        foreach (var kv in grouped)
            tables.Add(new { name = kv.Key, columns = kv.Value });
        return tables;
    }

    private static async Task<List<object>> LoadForeignKeysAsync(System.Data.Common.DbConnection conn, bool showSystem)
    {
        var filter = showSystem ? "" : "WHERE tp.is_ms_shipped = 0";
        var list = new List<object>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT
                fk.name AS fk_name,
                tp.name AS parent_table,
                cp.name AS parent_column,
                tr.name AS referenced_table,
                cr.name AS referenced_column,
                fk.delete_referential_action_desc AS on_delete
            FROM sys.foreign_keys fk
            JOIN sys.foreign_key_columns fkc ON fk.object_id = fkc.constraint_object_id
            JOIN sys.tables tp ON fkc.parent_object_id = tp.object_id
            JOIN sys.columns cp ON fkc.parent_object_id = cp.object_id AND fkc.parent_column_id = cp.column_id
            JOIN sys.tables tr ON fkc.referenced_object_id = tr.object_id
            JOIN sys.columns cr ON fkc.referenced_object_id = cr.object_id AND fkc.referenced_column_id = cr.column_id
            {filter}
            ORDER BY tp.name, fk.name
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

    private static async Task<List<object>> LoadIndexesAsync(System.Data.Common.DbConnection conn, bool showSystem)
    {
        var filter = showSystem ? "WHERE i.name IS NOT NULL" : "WHERE i.name IS NOT NULL AND t.is_ms_shipped = 0";
        var list = new List<object>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT
                t.name AS table_name,
                i.name AS index_name,
                i.type_desc,
                i.is_unique,
                STRING_AGG(c.name, ', ') WITHIN GROUP (ORDER BY ic.key_ordinal) AS columns
            FROM sys.indexes i
            JOIN sys.tables t ON i.object_id = t.object_id
            JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
            JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
            {filter}
            GROUP BY t.name, i.name, i.type_desc, i.is_unique
            ORDER BY t.name, i.name
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
                columns = reader.GetString(4)
            });
        }
        return list;
    }

    private static async Task<List<object>> LoadCheckConstraintsAsync(System.Data.Common.DbConnection conn)
    {
        var list = new List<object>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT
                t.name AS table_name,
                cc.name AS constraint_name,
                cc.definition
            FROM sys.check_constraints cc
            JOIN sys.tables t ON cc.parent_object_id = t.object_id
            ORDER BY t.name, cc.name
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

    private static async Task<List<object>> LoadStoredProceduresAsync(System.Data.Common.DbConnection conn)
    {
        var list = new List<object>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT
                p.name,
                p.create_date,
                p.modify_date,
                m.definition
            FROM sys.procedures p
            JOIN sys.sql_modules m ON p.object_id = m.object_id
            WHERE p.is_ms_shipped = 0
            ORDER BY p.name
            """;
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new
            {
                name = reader.GetString(0),
                createdAt = reader.GetDateTime(1),
                modifiedAt = reader.GetDateTime(2),
                definition = reader.IsDBNull(3) ? null : reader.GetString(3)
            });
        }
        return list;
    }

    private static async Task<List<object>> LoadViewsAsync(System.Data.Common.DbConnection conn)
    {
        var list = new List<object>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT
                v.name,
                v.create_date,
                v.modify_date,
                m.definition
            FROM sys.views v
            JOIN sys.sql_modules m ON v.object_id = m.object_id
            WHERE v.is_ms_shipped = 0
            ORDER BY v.name
            """;
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new
            {
                name = reader.GetString(0),
                createdAt = reader.GetDateTime(1),
                modifiedAt = reader.GetDateTime(2),
                definition = reader.IsDBNull(3) ? null : reader.GetString(3)
            });
        }
        return list;
    }

    private static async Task<List<object>> LoadRowCountsAsync(System.Data.Common.DbConnection conn, bool showSystem)
    {
        var filter = showSystem ? "" : "WHERE t.is_ms_shipped = 0";
        var list = new List<object>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT
                t.name,
                SUM(p.rows) AS row_count
            FROM sys.tables t
            JOIN sys.partitions p ON t.object_id = p.object_id AND p.index_id IN (0, 1)
            {filter}
            GROUP BY t.name
            ORDER BY t.name
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
