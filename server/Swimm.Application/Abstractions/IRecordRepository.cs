using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Публичное чтение рекордов и нормативов (read-only путь, swimm_ro).
/// Кэш режется по региону/категории — добавление данных новой страны
/// не инвалидирует горячие выборки остальных.
/// </summary>
public interface IRecordRepository
{
    /// <summary>
    /// Рекорды региона. region: "world" | код континента (EU/AS) | ISO-код страны (ISR).
    /// category: open/age/junior/masters; null — все категории региона.
    /// </summary>
    /// <param name="withHolderDetails">
    /// Досыпать год рождения и возраст держателя (отладочная опция ShowAgeRecordsDetails).
    /// Отдельный параметр, а не чтение настройки внутри: репозиторий кэширует ответ, и
    /// подробности обязаны попадать в СВОЙ ключ кэша, иначе они «залипнут» после выключения.
    /// </param>
    Task<IReadOnlyList<RecordDto>> GetRecordsAsync(
        string region, string? category = null, bool withHolderDetails = false);

    /// <summary>
    /// Рейтинг стран по одной дисциплине (этап 11.2.1) — то, чего <c>GetRecordsAsync</c> дать
    /// не может: тот режет по региону, а рейтингу нужен срез ПОПЕРЁК регионов.
    ///
    /// Категория всегда <c>open</c>, тип региона всегда <c>country</c>: рейтингуются страны,
    /// а мировой рекорд едет отдельным полем ответа как эталон, а не строкой наравне с ними.
    /// </summary>
    Task<RecordRankingDto> GetRankingAsync(RecordRankingQuery query);

    /// <summary>
    /// Сравнение двух стран по рекордам (этап 11.3.1): общая ось дисциплин, время каждой
    /// стороны, дельта и сводный счёт.
    ///
    /// Не выражается через <see cref="GetRankingAsync"/> с двумя кодами: тот отвечает про
    /// ОДНУ дисциплину, а здесь смысл ровно в обходе всех сразу — и в том, что дисциплина
    /// без данных у одной из сторон обязана остаться видимой строкой «нет данных», а не
    /// исчезнуть (11.3.3).
    /// </summary>
    Task<RecordCompareDto> GetCompareAsync(RecordCompareQuery query);

    /// <summary>
    /// Страны, у которых в справочнике есть рекорды `open` — список для выбора на витрине
    /// (`/records/compare`). Что есть В БАЗЕ, а не что отдаёт источник.
    /// </summary>
    Task<IReadOnlyList<RecordCountryOptionDto>> GetRecordCountriesAsync();

    /// <summary>
    /// Нормативы. kind: regular/masters; null — все.
    /// country: alpha-3 код системы нормативов (RUS/ISR/…); null — без фильтра (легаси).
    /// Задан — отдаёт строки с этой страной плюс универсальные (Country == "").
    /// </summary>
    Task<IReadOnlyList<NormativeStandardDto>> GetStandardsAsync(string? kind = null, string? country = null);
}
