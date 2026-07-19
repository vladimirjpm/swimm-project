namespace Swimm.Application.Abstractions;

/// <summary>Кандидат на привязку loglig ID с результатом сверки (для выбора админом).</summary>
public sealed record LogligCandidateInfo(
    int LogligId, string FullName, int? BirthYear, string? ClubName,
    LogligMatchDecision Decision, bool BirthYearMatch, bool ClubNameMatch, int MatchedResultCount);

/// <summary>Результат операции привязки/поиска loglig ID.</summary>
public sealed record LogligLinkResult(
    bool Linked,                 // true — привязка сохранена
    string? Error,               // человекочитаемая ошибка (уже привязан, карточка недоступна…)
    bool SearchConfigured,       // false — поисковый провайдер без ключа
    IReadOnlyList<LogligCandidateInfo> Candidates); // кандидаты для выбора админом

/// <summary>Строка админ-таблицы пловцов для страницы привязки Loglig ID.</summary>
public sealed record LogligSwimmerRow(
    int SwimmerId, string LastName, string FirstName, int BirthYear, string? ClubName,
    int? LogligId, string? Status, string? Source, DateTime? VerifiedAt);

/// <summary>
/// Оркестрация привязки профиля loglig.com к пловцу (docs/loglig-id-plan.md, шаг 5):
/// автопоиск + сверка либо ручная привязка админом.
/// </summary>
public interface ILogligLinkService
{
    /// <summary>Пловцы для админ-таблицы: фильтр по подстроке имени (иврит/англ) и статусу ("linked"/"unlinked"/null=все), максимум take.</summary>
    Task<IReadOnlyList<LogligSwimmerRow>> ListAsync(string? query, string? status, int take, CancellationToken ct);

    /// <summary>Пайплайн поиска: кандидаты по имени → карточки → сверка. Ровно один AutoVerify → привязка (auto), иначе кандидаты в ответ.</summary>
    Task<LogligLinkResult> FindAndLinkAsync(int swimmerId, CancellationToken ct);

    /// <summary>Ручная привязка админом (source=admin). Карточка обязана существовать; сверка возвращается как информация.</summary>
    Task<LogligLinkResult> SetManualAsync(int swimmerId, int logligId, CancellationToken ct);

    /// <summary>Снять привязку (обнуляет все Loglig-поля).</summary>
    Task<bool> UnlinkAsync(int swimmerId, CancellationToken ct);
}
