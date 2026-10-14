namespace Swimm.Application.Abstractions;

/// <summary>
/// Мост сброса кэша между процессами (решение В17, docs/plans/azure-deploy-plan.md §0-тер п. 3).
/// Каждый процесс держит СВОЙ кэш в памяти; запись в общую БД из другого процесса (локальная
/// админка в режиме ProdAdmin, CLI, второй инстанс сайта) без моста видна только через TTL.
/// Мост: процесс-писатель после сброса у себя шлёт метки в канал Postgres (<c>pg_notify</c>),
/// каждый процесс слушает канал (<c>LISTEN</c>) и сбрасывает их у себя.
///
/// Здесь — только состояние для /Admin/Cache: жив ли канал и сколько через него прошло.
/// </summary>
public interface ICacheBridgeStatus
{
    CacheBridgeState State();
}

/// <param name="Enabled">Мост включён конфигом (<c>CacheBridge:Enabled</c>).</param>
/// <param name="Channel">Имя канала Postgres.</param>
/// <param name="Listening">Слушающее соединение сейчас открыто.</param>
/// <param name="ListeningSince">С какого момента слушаем без обрыва; null — не слушаем.</param>
/// <param name="Reconnects">Сколько раз переподключались (каждый раз — общий сброс: могли пропустить).</param>
/// <param name="Published">Сколько сбросов этот процесс отправил в канал.</param>
/// <param name="PublishFailures">Сколько отправок не удалось (запись в БД при этом прошла).</param>
/// <param name="Oversized">Сколько раз список меток не влез в пейлоад и ушёл общим сбросом.</param>
/// <param name="Received">Сколько чужих сбросов пришло и применено.</param>
/// <param name="LastReceivedAt">Когда пришёл последний чужой сброс.</param>
/// <param name="LastReceivedReason">Подпись последнего чужого сброса.</param>
/// <param name="LastError">Последняя ошибка канала (отправки или прослушивания).</param>
/// <param name="LastErrorAt">Когда она случилась.</param>
public sealed record CacheBridgeState(
    bool Enabled,
    string Channel,
    bool Listening,
    DateTimeOffset? ListeningSince,
    long Reconnects,
    long Published,
    long PublishFailures,
    long Oversized,
    long Received,
    DateTimeOffset? LastReceivedAt,
    string? LastReceivedReason,
    string? LastError,
    DateTimeOffset? LastErrorAt);
