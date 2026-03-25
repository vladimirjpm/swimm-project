using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Swimm.API.Data;

namespace Swimm.API.Services;

public class DbSchemaService
{
    private readonly SwimmDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly AdminSettingsService _settings;
    private const string CacheKey = "db-schema";

    public DbSchemaService(SwimmDbContext db, IMemoryCache cache, AdminSettingsService settings)
    {
        _db = db;
        _cache = cache;
        _settings = settings;
    }

    public async Task<object> GetSchemaAsync(bool forceRefresh = false)
    {
        var settingForce = _settings.GetValue<bool>("ForceRefresh", false);
        if (forceRefresh || settingForce)
            _cache.Remove(CacheKey);

        var ttl = _settings.GetValue<int>("SchemaCacheTTL", 30);
        if (ttl <= 0) ttl = 30;

        return (await _cache.GetOrCreateAsync(CacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(ttl);
            var schema = _settings.GetValue<string>("DefaultSchema", "dbo");
            var showSystem = _settings.GetValue<bool>("ShowSystemTables", false);

            var conn = _db.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open) await conn.OpenAsync();

            return new
            {
                tables = await LoadTablesAsync(conn, schema),
                foreignKeys = await LoadForeignKeysAsync(conn, showSystem),
                indexes = await LoadIndexesAsync(conn, showSystem),
                checkConstraints = await LoadCheckConstraintsAsync(conn),
                storedProcedures = await LoadStoredProceduresAsync(conn),
                views = await LoadViewsAsync(conn),
                rowCounts = await LoadRowCountsAsync(conn, showSystem)
            };
        }))!;
    }

    private static async Task<List<object>> LoadTablesAsync(DbConnection conn, string schema)
    {
        var tables = new List<object>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT t.TABLE_NAME, c.COLUMN_NAME, c.DATA_TYPE,
                   c.CHARACTER_MAXIMUM_LENGTH, c.IS_NULLABLE, c.COLUMN_DEFAULT,
                   CASE WHEN kcu.COLUMN_NAME IS NOT NULL THEN 1 ELSE 0 END AS IS_PK
            FROM INFORMATION_SCHEMA.TABLES t
            JOIN INFORMATION_SCHEMA.COLUMNS c ON t.TABLE_NAME = c.TABLE_NAME AND t.TABLE_SCHEMA = c.TABLE_SCHEMA
            LEFT JOIN INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
                ON tc.TABLE_NAME = t.TABLE_NAME AND tc.TABLE_SCHEMA = t.TABLE_SCHEMA AND tc.CONSTRAINT_TYPE = 'PRIMARY KEY'
            LEFT JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE kcu
                ON kcu.CONSTRAINT_NAME = tc.CONSTRAINT_NAME AND kcu.COLUMN_NAME = c.COLUMN_NAME
            WHERE t.TABLE_TYPE = 'BASE TABLE' AND t.TABLE_SCHEMA = @schema
            ORDER BY t.TABLE_NAME, c.ORDINAL_POSITION";
        var p = cmd.CreateParameter(); p.ParameterName = "@schema"; p.Value = schema;
        cmd.Parameters.Add(p);

        using var r = await cmd.ExecuteReaderAsync();
        var grouped = new Dictionary<string, List<object>>();
        while (await r.ReadAsync())
        {
            var tbl = r.GetString(0);
            if (!grouped.ContainsKey(tbl)) grouped[tbl] = new List<object>();
            grouped[tbl].Add(new
            {
                column = r.GetString(1),
                type = r.GetString(2),
                maxLength = r.IsDBNull(3) ? null : (int?)r.GetInt32(3),
                nullable = r.GetString(4) == "YES",
                defaultValue = r.IsDBNull(5) ? null : r.GetString(5),
                isPrimaryKey = r.GetInt32(6) == 1
            });
        }
        foreach (var kv in grouped)
            tables.Add(new { table = kv.Key, columns = kv.Value });
        return tables;
    }

    private static async Task<List<object>> LoadForeignKeysAsync(DbConnection conn, bool showSystem)
    {
        var list = new List<object>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT fk.name, tp.name AS parent_table, cp.name AS parent_column,
                   tr.name AS referenced_table, cr.name AS referenced_column
            FROM sys.foreign_keys fk
            JOIN sys.foreign_key_columns fkc ON fk.object_id = fkc.constraint_object_id
            JOIN sys.tables tp ON fkc.parent_object_id = tp.object_id
            JOIN sys.columns cp ON fkc.parent_object_id = cp.object_id AND fkc.parent_column_id = cp.column_id
            JOIN sys.tables tr ON fkc.referenced_object_id = tr.object_id
            JOIN sys.columns cr ON fkc.referenced_object_id = cr.object_id AND fkc.referenced_column_id = cr.column_id"
            + (showSystem ? "" : " WHERE tp.is_ms_shipped = 0");

        using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            list.Add(new { name = r.GetString(0), parentTable = r.GetString(1), parentColumn = r.GetString(2), referencedTable = r.GetString(3), referencedColumn = r.GetString(4) });
        return list;
    }

    private static async Task<List<object>> LoadIndexesAsync(DbConnection conn, bool showSystem)
    {
        var list = new List<object>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT t.name AS table_name, i.name AS index_name, i.type_desc,
                   i.is_unique, STRING_AGG(c.name, ', ') AS columns
            FROM sys.indexes i
            JOIN sys.tables t ON i.object_id = t.object_id
            JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
            JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
            WHERE i.name IS NOT NULL" + (showSystem ? "" : " AND t.is_ms_shipped = 0") + @"
            GROUP BY t.name, i.name, i.type_desc, i.is_unique
            ORDER BY t.name, i.name";

        using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            list.Add(new { table = r.GetString(0), name = r.GetString(1), type = r.GetString(2), isUnique = r.GetBoolean(3), columns = r.GetString(4) });
        return list;
    }

    private static async Task<List<object>> LoadCheckConstraintsAsync(DbConnection conn)
    {
        var list = new List<object>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT OBJECT_NAME(parent_object_id) AS table_name, name, definition
            FROM sys.check_constraints ORDER BY table_name, name";
        using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            list.Add(new { table = r.GetString(0), name = r.GetString(1), definition = r.GetString(2) });
        return list;
    }

    private static async Task<List<object>> LoadStoredProceduresAsync(DbConnection conn)
    {
        var list = new List<object>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT ROUTINE_NAME, ROUTINE_DEFINITION
            FROM INFORMATION_SCHEMA.ROUTINES
            WHERE ROUTINE_TYPE = 'PROCEDURE' ORDER BY ROUTINE_NAME";
        using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            list.Add(new { name = r.GetString(0), definition = r.IsDBNull(1) ? null : r.GetString(1) });
        return list;
    }

    private static async Task<List<object>> LoadViewsAsync(DbConnection conn)
    {
        var list = new List<object>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT TABLE_NAME, VIEW_DEFINITION
            FROM INFORMATION_SCHEMA.VIEWS ORDER BY TABLE_NAME";
        using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            list.Add(new { name = r.GetString(0), definition = r.IsDBNull(1) ? null : r.GetString(1) });
        return list;
    }

    private static async Task<List<object>> LoadRowCountsAsync(DbConnection conn, bool showSystem)
    {
        var list = new List<object>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT t.name, SUM(p.rows) AS row_count
            FROM sys.tables t
            JOIN sys.partitions p ON t.object_id = p.object_id AND p.index_id IN (0,1)"
            + (showSystem ? "" : " WHERE t.is_ms_shipped = 0") + @"
            GROUP BY t.name ORDER BY t.name";
        using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            list.Add(new { table = r.GetString(0), count = r.GetInt64(1) });
        return list;
    }
}

