namespace Swimm.Application.Dtos;

/// <summary>
/// Запрос на получение рекордов из внешнего источника (этап 2.6). Либо файлы приложены
/// (ручная загрузка — обязательный fallback для isr.org.il), либо провайдер качает сам
/// по своим (whitelist-only) URL-ам — <see cref="PrimaryStream"/> тогда null.
/// </summary>
public sealed record RecordSourceRequest(
    string Source,
    Stream? PrimaryStream = null,
    string? PrimaryFileName = null,
    Stream? SecondaryStream = null,
    string? SecondaryFileName = null,
    string? PoolType = null);

/// <summary>Один спарсенный рекорд, ещё не сопоставленный с БД (см. <see cref="Swimm.Domain.Entities.Record"/>).</summary>
public sealed record ParsedRecordDto(
    string RegionType,
    string RegionCode,
    string Category,
    string AgeKey,
    string Gender,
    string PoolType,
    string Style,
    string Distance,
    string Time,
    string? HolderName,
    string? Club,
    string? HolderCountry,
    string? RecordDate);

/// <summary>Одна строка диффа (новая или изменившаяся) — для таблицы превью в UI.</summary>
public sealed record RecordDiffEntry(
    string RegionType,
    string RegionCode,
    string Category,
    string AgeKey,
    string Gender,
    string PoolType,
    string Style,
    string Distance,
    string? OldTime,
    string? OldHolderName,
    string? OldRecordDate,
    string NewTime,
    string? NewHolderName,
    string? NewRecordDate);

/// <summary>
/// Результат сравнения фетча с текущими Records. missingInSource — только информационно,
/// ничего не удаляется. DiffId — ключ во временном кэше (10 мин) для последующего Apply.
/// </summary>
public sealed record RecordDiffResult(
    string DiffId,
    string Source,
    int AddedCount,
    int ChangedCount,
    int UnchangedCount,
    int MissingInSourceCount,
    IReadOnlyList<RecordDiffEntry> Added,
    IReadOnlyList<RecordDiffEntry> Changed);

public sealed record RecordDiffApplyRequest(string DiffId, bool ApplyAdded, bool ApplyChanged);

public sealed record RecordDiffApplyResult(bool Success, string? Error, int AppliedCount);

/// <summary>Статус источника для карточки в UI: когда последний раз реально обновлялись его рекорды.</summary>
public sealed record RecordSourceStatusDto(string Source, DateTime? LastUpdatedAt);
