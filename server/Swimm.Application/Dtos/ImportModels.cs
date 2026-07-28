namespace Swimm.Application.Dtos;

/// <summary>
/// Привязка импортируемого файла к многодневному событию.
/// EventId задан → дописываем день к существующему событию.
/// NewEventName задан (без EventId) → создаём новое событие с этим именем.
/// Оба null → обычное однодневное соревнование (поведение по умолчанию).
/// </summary>
/// <param name="OverwriteExisting">
/// Upsert-режим переимпорта (docs/plans/import-upsert-plan.md, Р6). По умолчанию false —
/// поведение не меняется: повторный импорт соревнования с уже загруженными результатами
/// отбивается ошибкой «Дубль». true → результаты матчатся по ключу заплыва/дорожки:
/// сматченные обновляются на месте, новые вставляются. НИКОГДА не удаляет — удаление
/// исчезнувших результатов включается отдельно флагом <see cref="DeleteMissing"/>.
/// </param>
/// <param name="DeleteMissing">
/// Опционально включает удаление «исчезнувших» результатов (есть в БД, нет в переимпортируемом
/// файле) при <see cref="OverwriteExisting"/>=true. По умолчанию false — переимпорт частичного
/// файла больше не сносит молча остальные результаты соревнования (инцидент 2026-07-20: 915 → 254
/// строк при переимпорте partial-файла с OverwriteExisting). Игнорируется, если OverwriteExisting=false.
/// Как и раньше, действует существующая защита медиа: строки с UserMedia/HubGroupMedia не
/// удаляются, а идут в счётчик SkippedWithMedia.
/// </param>
/// <param name="PointRuleClubsId">
/// Правило клубных очков для создаваемых соревнований (Э5). null — «Авто»: правило
/// подберётся по дате и типу (<c>CompetitionRuleResolver</c>), как и раньше. Проставляется
/// КАЖДОМУ созданному дню: правило хранится у соревнования, а не у события.
/// Существующие соревнования при переимпорте не трогаются — привязка меняется только
/// осознанно, на /Admin/Competitions.
/// </param>
/// <param name="PointRuleSwimmersId">То же для правила High Point.</param>
public sealed record ImportEventOptions(
    int? EventId,
    string? NewEventName,
    bool OverwriteExisting = false,
    bool DeleteMissing = false,
    int? PointRuleClubsId = null,
    int? PointRuleSwimmersId = null);

public class ImportResult
{
    public int TotalRows { get; set; }
    public int Created { get; set; }
    public int Skipped { get; set; }
    public int Errors { get; set; }
    public List<string> ErrorMessages { get; set; } = [];
    public List<string> DiagnosticLog { get; set; } = [];
    public string Message { get; set; } = string.Empty;

    /* === Upsert-счётчики (OverwriteExisting=true); нули в обычном режиме === */

    /// <summary>Сматченные результаты, обновлённые на месте (Id сохранён).</summary>
    public int Updated { get; set; }
    /// <summary>Новые результаты, вставленные при upsert-переимпорте (подмножество Created).</summary>
    public int Inserted { get; set; }
    /// <summary>Исчезнувшие результаты, реально удалённые (без медиа).</summary>
    public int Deleted { get; set; }
    /// <summary>Исчезнувшие результаты, НЕ удалённые из-за навешанного UserMedia/HubGroupMedia — разберитесь руками.</summary>
    public int SkippedWithMedia { get; set; }
}

/// <summary>Существующее (по ключу (Name|SubName)|Date|PoolType) соревнование, найденное для дня из превью.</summary>
/// <param name="ExistingResultCount">
/// Кол-во строк Results у сматченного соревнования (0/null — если матча нет). Используется UI
/// для предупреждения о потенциальном массовом удалении при OverwriteExisting=true с неполным файлом.
/// </param>
public sealed record ExistingCompetitionMatch(
    string Competition,
    string Date,
    int? ExistingCompetitionId,
    string? ExistingCompetitionName,
    int? ExistingResultCount = null);

public class ClearResult
{
    public int Total { get; set; }
    public int Results { get; set; }
    public int Competitions { get; set; }
    public int CompetitionEvents { get; set; }
    public int Clubs { get; set; }
    public int Swimmers { get; set; }
    public int Relays { get; set; }
    public int Galleries { get; set; }
    public int GalleryItems { get; set; }
    public int Countries { get; set; }
    public int ImportHistory { get; set; }
}

public class DeleteCompetitionResult
{
    public int CompetitionId { get; set; }
    public string CompetitionName { get; set; } = string.Empty;
    public int Results { get; set; }
    public int Relays { get; set; }
    public int GalleryItems { get; set; }
    public int Galleries { get; set; }
    public int ImportHistory { get; set; }
    /// <summary>Удалённые URL-ы результатов (по OrgCompId), если этот OrgCompId больше нигде не использовался.</summary>
    public int ResultUrls { get; set; }
    /// <summary>Пловцы-сироты, удалённые вместе с соревнованием (не осталось результатов и связей).</summary>
    public int Swimmers { get; set; }
}
