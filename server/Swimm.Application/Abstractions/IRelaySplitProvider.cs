namespace Swimm.Application.Abstractions;

/// <summary>
/// Промежуточные эстафет из пособытийных PDF loglig (docs/relays.md, «Промежуточные эстафет»).
/// Работает поверх уже разобранного протокола: берёт JSON импорта, доклеивает к эстафетным
/// строкам ноги с промежуточными и возвращает новый JSON.
///
/// Никогда не роняет импорт: сбой сети, не-PDF, сломанная вёрстка — исходный JSON без
/// изменений и причина в <see cref="RelaySplitOutcome.Message"/>.
/// </summary>
public interface IRelaySplitProvider
{
    Task<RelaySplitOutcome> EnrichAsync(int logligId, string resultsJson, CancellationToken ct = default);
}

/// <param name="ResultsJson">JSON импорта: доклеенный или исходный.</param>
/// <param name="Enriched">Сколько эстафет получили промежуточные.</param>
/// <param name="Message">Сводка для лога и превью («команд 35, доклеено 11…» или причина отказа).</param>
public sealed record RelaySplitOutcome(string ResultsJson, int Enriched, string Message);
