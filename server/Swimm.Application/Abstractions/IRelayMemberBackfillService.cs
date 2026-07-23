namespace Swimm.Application.Abstractions;

/// <summary>
/// Разовый бэкфилл структурного состава эстафет (RelayMembers) для существующих
/// данных, импортированных до появления структурных ног. Матчинг ног из текста
/// <c>Relay.SwimmersName</c> НЕ глобальный: кандидаты сужены до ростера того же
/// соревнования (при неоднозначности — до клуба эстафеты), линкуем только при
/// однозначном совпадении. Fail-safe: сомнительную ногу пропускаем, не создаём
/// и не мёржим пловцов. Идемпотентно: эстафеты с уже заполненными членами пропускаются.
/// </summary>
public interface IRelayMemberBackfillService
{
    Task<RelayBackfillReport> BackfillAsync(bool apply);
}

public class RelayBackfillReport
{
    public bool Applied { get; set; }
    public int RelaysTotal { get; set; }
    /// <summary>Эстафеты, у которых состав уже был (пропущены как есть).</summary>
    public int RelaysAlreadyPopulated { get; set; }
    /// <summary>Эстафеты, для которых линкована хотя бы одна нога в этом прогоне.</summary>
    public int RelaysLinked { get; set; }
    public int LegsLinked { get; set; }
    public int LegsUnmatched { get; set; }
    /// <summary>Примеры несопоставленных ног (для диагностики), ограничено.</summary>
    public List<string> UnmatchedSamples { get; set; } = new();
}
