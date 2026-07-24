namespace Swimm.Application.Abstractions;

/// <summary>Результат предложения привязки пользователем: принято в очередь либо причина отказа.</summary>
public sealed record LogligSuggestResult(bool Accepted, string? Error);

/// <summary>Итог ночной верификации предложений (для лога/мониторинга).</summary>
public sealed record LogligSuggestionVerifyReport(int Checked, int Verified, int Rejected, int Skipped);

/// <summary>Публичный статус привязки. ProfileUrl (ссылка на публичную карточку loglig.com
/// с актуальным сезоном) заполняется только при Verified — для Suggested/Rejected null,
/// как и аудит.</summary>
public sealed record LogligStatusResult(string? Status, string? ProfileUrl);

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

    /// <summary>Статус привязки для публичного показа (LogligId — только при Verified,
    /// без аудита). Null-статус и для несуществующего пловца — не палим существование.</summary>
    Task<LogligStatusResult> GetStatusAsync(int swimmerId, CancellationToken ct = default);
}
