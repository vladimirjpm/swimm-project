using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Национальный season best по НАШИМ протоколам — источник таба «Season best» рядом с
/// возрастными рекордами на странице результатов (design_handoff_age_records_sb).
///
/// Клубный аналог живёт в <see cref="IClubPublicRepository"/> и отвечает на другой вопрос
/// («наши первые в стране»); здесь — просто лучшее время сезона в каждой ступени.
/// </summary>
public interface ISeasonBestRepository
{
    /// <summary>
    /// Лучшее время сезона для одной дисциплины в каждой паре «пол × возраст в сезоне».
    ///
    /// Решения по составу выборки (Влад, 2026-08-22):
    /// • ось возраста — СЕЗОННАЯ (SeasonMath.AgeInSeason), не календарная ось справочника;
    /// • соревнования masters (<c>Competition.IsMasters</c>) не участвуют вовсе;
    /// • эстафетные ноги, незачётные и помеченные <c>SuspectReason</c> заплывы отброшены —
    ///   как и в клубном season best.
    ///
    /// <paramref name="distance">Как в Results.Distance — без «m»: «50», «100».</paramref>
    /// <paramref name="poolType">«25m» / «50m»; null — оба бассейна в одной выборке.</paramref>
    /// <paramref name="season">Год НАЧАЛА сезона; null — текущий.</paramref>
    /// </summary>
    Task<SeasonBestNationalDto> GetNationalSeasonBestAsync(
        string style, string distance, string? poolType, int? season, CancellationToken ct = default);

    /// <summary>
    /// Ранжированный список одной дисциплины за сезон — страница <c>/season-best</c>.
    ///
    /// Отличие от <see cref="GetNationalSeasonBestAsync"/>: там одна строка на ступень
    /// возраста, здесь — все заплывы ВНУТРИ выбранного среза, по времени. Эстафеты, TimeFail
    /// и помеченные <c>SuspectReason</c> отброшены так же, но есть два отличия:
    ///
    /// • дедупа по пловцу по умолчанию НЕТ: один человек законно занимает и первое место, и
    ///   третье — это его разные старты за сезон (решение Влада 2026-08-26);
    /// • masters НЕ исключены совсем, а живут отдельным срезом (<see cref="SeasonBestListQuery.Masters"/>):
    ///   либо мастерские старты с осью «возрастная группа», либо обычные с осью «возраст в
    ///   сезоне». Смешать их нельзя — иначе в одном рейтинге окажутся 12-летние и 47-летние
    ///   (решение Влада 2026-08-26; у <see cref="GetNationalSeasonBestAsync"/> masters
    ///   по-прежнему не участвуют вовсе).
    /// </summary>
    /// <summary>
    /// ВСЯ сезонная таблица: «пол × возраст в сезоне × стиль × дистанция × бассейн» →
    /// лучшее время сезона и число сверстников на ступени.
    ///
    /// Нужна затем же, зачем справочник рекордов грузится целиком: строка протокола должна
    /// уметь проверить себя ЛОКАЛЬНО. Поштучные запросы (<see cref="GetNationalSeasonBestAsync"/>
    /// на дисциплину) для протокола на сотни строк означали бы десятки запросов, поэтому
    /// эталон отдаётся одним ответом и кэшируется на сутки.
    ///
    /// Состав выборки — тот же, что у <see cref="GetNationalSeasonBestAsync"/> (masters,
    /// открытая вода, эстафеты, <c>SuspectReason</c> и <c>TimeFail</c> не участвуют): два
    /// разных определения «лучшего в сезоне» на одном экране спорили бы друг с другом.
    /// </summary>
    Task<SeasonBestTableDto> GetSeasonBestTableAsync(int? season, CancellationToken ct = default);

    Task<SeasonBestListDto> GetSeasonBestListAsync(
        SeasonBestListQuery query, CancellationToken ct = default);

    /// <summary>
    /// Чем наполнять карусель сезонов и селектор дисциплины на странице <c>/season-best</c>:
    /// сезоны с данными, стили с реально проплытыми дистанциями и возрастные группы
    /// мастерских протоколов (вторая шкала возраста). Группа, чья подпись не сходится с
    /// возрастами людей в ней, в опции не попадает — см. docs/data-integrity.md §9.
    /// </summary>
    Task<SeasonBestOptionsDto> GetSeasonBestOptionsAsync(CancellationToken ct = default);
}
