namespace Swimm.Application.Abstractions;

/// <summary>
/// Отстаёт ли локальная база от прода (решение В17, docs/plans/azure-deploy-plan.md §0-тер-бис).
/// После запуска главная база — прод, локальная — её копия, снятая <c>server/db/sync-from-prod.sh</c>.
/// Локальный API в разработке сверяет версию прода (<c>Sys_DataVersion</c>) с отметкой последней
/// синхронизации (<c>Sys_SyncState</c>) и предупреждает: Warning в лог и жёлтая полоса в админке.
/// </summary>
public interface IDataSyncStatus
{
    /// <summary>Сверка вообще возможна: задана строка <c>ProdVersionConnection</c>.</summary>
    bool Enabled { get; }

    /// <summary>Последняя сверка; null — ещё не сверяли или сверка выключена.</summary>
    DataSyncState? Current { get; }

    /// <summary>Сверить сейчас. Не бросает: недоступный прод — состояние с <see cref="DataSyncState.Error"/>.</summary>
    Task<DataSyncState?> CheckAsync(CancellationToken ct = default);
}

/// <param name="ProdVersion">Версия данных прода; null — прод недоступен.</param>
/// <param name="ProdLastTable">Какую таблицу на проде меняли последней.</param>
/// <param name="ProdLastChangedAt">Когда.</param>
/// <param name="SyncedFromVersion">От какой версии прода снята локальная копия; null — ни разу.</param>
/// <param name="SyncedAt">Когда снята.</param>
/// <param name="Error">Почему сверка не удалась (прод недоступен, нет миграции).</param>
/// <param name="CheckedAt">Когда сверяли.</param>
public sealed record DataSyncState(
    long? ProdVersion,
    string? ProdLastTable,
    DateTimeOffset? ProdLastChangedAt,
    long? SyncedFromVersion,
    DateTimeOffset? SyncedAt,
    string? Error,
    DateTimeOffset CheckedAt)
{
    /// <summary>Локальная копия старее прода (или её не снимали). Прод недоступен — не знаем, не пугаем.</summary>
    public bool Behind => ProdVersion is { } prod && (SyncedFromVersion is not { } local || prod > local);

    /// <summary>Строка для лога и полосы. null — не отстаёт.</summary>
    public string? Warning()
    {
        if (!Behind) return null;
        var local = SyncedFromVersion is { } v
            ? $"у тебя v{v}{(SyncedAt is { } at ? $" от {at.ToLocalTime():dd.MM HH:mm}" : "")}"
            : "локальную копию с прода ещё не снимали";
        var last = ProdLastTable is not null
            ? $"; последнее изменение — {ProdLastTable}{(ProdLastChangedAt is { } c ? $", {c.ToLocalTime():dd.MM HH:mm}" : "")}"
            : "";
        return $"Локальная база отстаёт от прода: прод v{ProdVersion}, {local}{last}. Обновить: server/db/sync-from-prod.sh";
    }
}
