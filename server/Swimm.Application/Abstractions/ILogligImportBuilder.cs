using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Куда кладём строки пособытийного источника: соревнование, в которое они импортируются.
/// Берётся из УЖЕ существующей записи в БД, а не из шапки страницы loglig.
/// </summary>
/// <param name="Date">
/// Дата соревнования в нашей базе. У loglig каждое событие несёт свой день чемпионата,
/// но многодневка лежит у нас ОДНОЙ записью (comp 1581 = все пять дней): взять даты
/// событий значило бы расщепить соревнование на несколько и оборвать ссылки, зачёты и медиа.
/// Разбиение на дни — отдельное решение, не побочный эффект смены источника.
/// </param>
public sealed record LogligImportContext(
    string Country,
    string CompetitionName,
    string Date,
    string PoolType,
    bool IsAward);

/// <summary>
/// Сборка JSON импорта (легаси-контракт <c>IImportService</c>) из результатов пособытийного
/// источника loglig. Живёт в слое парсинга: там модель строки импорта и правила возрастных
/// групп. Имена приходят уже сопоставленными — резолвер знает БД и потому лежит в инфраструктуре.
/// </summary>
public interface ILogligImportBuilder
{
    /// <param name="resolveName">
    /// Имя источника («имя фамилия» одной строкой) → наши поля LastName/FirstName.
    /// </param>
    /// <returns>JSON-массив строк результата — то же, что отдаёт разбор PDF.</returns>
    string BuildResultsJson(
        IReadOnlyList<LogligEventResultsDto> events,
        LogligImportContext context,
        Func<LogligResultRowDto, (string LastName, string FirstName)> resolveName);
}
