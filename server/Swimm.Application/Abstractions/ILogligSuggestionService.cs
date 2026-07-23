namespace Swimm.Application.Abstractions;

/// <summary>Результат предложения привязки пользователем: принято в очередь либо причина отказа.</summary>
public sealed record LogligSuggestResult(bool Accepted, string? Error);

/// <summary>Итог ночной верификации предложений (для лога/мониторинга).</summary>
public sealed record LogligSuggestionVerifyReport(int Checked, int Verified, int Rejected, int Skipped);

/// <summary>
/// Краудсорс-привязка Loglig ID (docs/loglig-id-plan.md, шаг 6): залогиненный пользователь
/// предлагает ID (пишется как Suggested без синхронной проверки), ночной джоб верифицирует
/// по имени + году рождения (спорные — полной сверкой результатов) → Verified / Rejected.
/// Привязка — только обогащение данных, прав не даёт.
/// </summary>
public interface ILogligSuggestionService
{
    /// <summary>Предложить привязку. Анти-SSRF: принимается только числовой loglig ID.</summary>
    Task<LogligSuggestResult> SuggestAsync(int swimmerId, int logligId, int userId, CancellationToken ct = default);

    /// <summary>Проверить все Suggested-привязки (вызывается ночным джобом).</summary>
    Task<LogligSuggestionVerifyReport> VerifySuggestedAsync(CancellationToken ct = default);

    /// <summary>Статус привязки для публичного показа (без LogligId и аудита). Null и для
    /// несуществующего пловца — не палим существование.</summary>
    Task<string?> GetStatusAsync(int swimmerId, CancellationToken ct = default);
}
