using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Сопоставляет спарсенные рекорды (<see cref="ParsedRecordDto"/>) с текущими Records по
/// уникальным осям (RegionType+RegionCode+Category+AgeKey+Gender+PoolType+Style+Distance) и
/// строит дифф; Apply — upsert выбранных групп (added/changed) в транзакции + сброс кэша.
/// Ничего не удаляет — missingInSource только информационно.
/// </summary>
public interface IRecordDiffService
{
    /// <param name="previewTtl">
    /// Сколько диффу жить до Apply. По умолчанию 10 минут — сессия превью в админке; у прогона
    /// по странам (11.1.2) он идёт часами, поэтому тот просит больше.
    /// </param>
    Task<RecordDiffResult> BuildDiffAsync(
        string source, IReadOnlyList<ParsedRecordDto> parsed,
        TimeSpan? previewTtl = null, CancellationToken ct = default);

    Task<RecordDiffApplyResult> ApplyAsync(RecordDiffApplyRequest request, CancellationToken ct = default);

    /// <summary>max(UpdatedAt) рекордов, относящихся к каждому источнику (по Category), для карточек в UI.</summary>
    Task<IReadOnlyList<RecordSourceStatusDto>> GetSourceStatusAsync(CancellationToken ct = default);
}
