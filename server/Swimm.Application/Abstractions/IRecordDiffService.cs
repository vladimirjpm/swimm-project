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
    Task<RecordDiffResult> BuildDiffAsync(string source, IReadOnlyList<ParsedRecordDto> parsed, CancellationToken ct = default);

    Task<RecordDiffApplyResult> ApplyAsync(RecordDiffApplyRequest request, CancellationToken ct = default);

    /// <summary>max(UpdatedAt) рекордов, относящихся к каждому источнику (по Category), для карточек в UI.</summary>
    Task<IReadOnlyList<RecordSourceStatusDto>> GetSourceStatusAsync(CancellationToken ct = default);
}
