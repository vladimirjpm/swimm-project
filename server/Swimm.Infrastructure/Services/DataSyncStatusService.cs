using Npgsql;
using Swimm.Application.Abstractions;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Сверка «локальная копия против прода» (<see cref="IDataSyncStatus"/>). Версия прода — по
/// строке <c>ProdVersionConnection</c> (роль <c>swimm_sync</c>, gitignored-конфиг разработки),
/// отметка синхронизации — из локальной базы. Чистый Npgsql: таблицы версии — служебные объекты
/// миграции, в модели EF их нет.
/// </summary>
public sealed class DataSyncStatusService(string? prodConnection, string? localConnection) : IDataSyncStatus
{
    private volatile DataSyncState? _current;

    public bool Enabled => !string.IsNullOrWhiteSpace(prodConnection) && !string.IsNullOrWhiteSpace(localConnection);

    public DataSyncState? Current => _current;

    /// <summary>
    /// Версия прода — так же, как её записывает <c>sync-from-prod.sh</c>: значение
    /// последовательности (0 — ни одной записи после миграции).
    /// </summary>
    internal const string ProdVersionSql = """
        SELECT CASE WHEN is_called THEN last_value ELSE 0 END FROM "Sys_DataVersionSeq"
        """;

    internal const string ProdLastChangeSql = """
        SELECT "Table", "ChangedAt" FROM "Sys_DataVersion"
         WHERE "ChangedAt" IS NOT NULL ORDER BY "Version" DESC LIMIT 1
        """;

    internal const string LocalSyncSql = """
        SELECT "SyncedFromVersion", "SyncedAt" FROM "Sys_SyncState" WHERE "Id" = 1
        """;

    public async Task<DataSyncState?> CheckAsync(CancellationToken ct = default)
    {
        if (!Enabled) return null;

        long? syncedFrom = null;
        DateTimeOffset? syncedAt = null;
        long? prodVersion = null;
        string? lastTable = null;
        DateTimeOffset? lastAt = null;
        string? error = null;

        try
        {
            await using var local = new NpgsqlConnection(localConnection);
            await local.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand(LocalSyncSql, local);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            if (await r.ReadAsync(ct))
            {
                syncedFrom = r.GetInt64(0);
                syncedAt = r.GetFieldValue<DateTimeOffset>(1);
            }
        }
        catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException)
        {
            error = $"локальная база: {ex.Message}";
        }

        try
        {
            await using var prod = new NpgsqlConnection(prodConnection);
            await prod.OpenAsync(ct);
            await using (var cmd = new NpgsqlCommand(ProdVersionSql, prod))
                prodVersion = Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));
            await using (var cmd = new NpgsqlCommand(ProdLastChangeSql, prod))
            await using (var r = await cmd.ExecuteReaderAsync(ct))
                if (await r.ReadAsync(ct))
                {
                    lastTable = r.GetString(0);
                    lastAt = r.GetFieldValue<DateTimeOffset>(1);
                }
        }
        catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException or TimeoutException)
        {
            prodVersion = null;
            error = error is null ? $"прод: {ex.Message}" : $"{error}; прод: {ex.Message}";
        }

        var state = new DataSyncState(prodVersion, lastTable, lastAt, syncedFrom, syncedAt, error, DateTimeOffset.UtcNow);
        _current = state;
        return state;
    }
}
