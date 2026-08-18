using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Публичный read-путь клуба (K4.2, docs/plans/club-page-plan.md): ростер и клубные рекорды,
/// которые карточки страницы клуба догружают по требованию («Show all N», переключатель
/// бассейна). Overview-карточку (K4.1) отдаёт отдельный репозиторий.
/// </summary>
public interface IClubPublicRepository
{
    /// <summary>
    /// Резолвит {id} в реальный id клуба: разматывает ОДИН переход мягкого merge
    /// (<see cref="Swimm.Domain.Entities.Club.MergedIntoId"/> — merge второго уровня
    /// запрещён guard-ом, дальше разматывать не нужно) и отбраковывает псевдоклубы
    /// (<see cref="Swimm.Domain.Entities.Club.IsPseudo"/> — страна/сборная, не клуб).
    /// null — клуба нет, он псевдо, либо merge завёл на псевдоклуб; контроллер отдаёт 404.
    /// </summary>
    Task<int?> ResolveClubIdAsync(int clubId);

    /// <summary>
    /// Ростер клуба (Swimmer.ClubId), с пагинацией и фильтрами. <paramref name="resolvedClubId"/> —
    /// уже резолвленный id (см. <see cref="ResolveClubIdAsync"/>). Возраст считается от
    /// <paramref name="season"/> (год начала сезона; null — текущий сезон), это НЕ зачётная
    /// группа Category. <paramref name="season"/> также ограничивает счётчики
    /// competitions/swims — без него они за всё время.
    /// </summary>
    Task<ClubRosterPageDto> GetRosterAsync(
        int resolvedClubId, int page, int pageSize, string? gender, int? ageFrom, int? ageTo, int? season);

    /// <summary>
    /// Season best: заплывы пловцов клуба, которые в этом сезоне ЛУЧШИЕ ПО СТРАНЕ в своём
    /// слоте (стиль × дистанция × бассейн × пол × возрастная ступень). Лидер считается по
    /// ВСЕЙ базе, а не внутри клуба, — поэтому карточка отвечает «наши первые в Израиле»,
    /// а не «наше лучшее».
    ///
    /// ⚠ «Первый в Израиле» = первый среди ИМПОРТИРОВАННОГО: в ответе есть
    /// <c>Meets</c> — сколько соревнований вошло в расчёт, и UI обязан это показать.
    ///
    /// <paramref name="season"/> — год начала сезона (SeasonMath); null — ТЕКУЩИЙ сезон.
    /// «За всё время» карточка не показывает сознательно. <paramref name="poolType"/> —
    /// "25m"/"50m", null — оба.
    ///
    /// Официальный справочник рекордов — отдельно, см. <see cref="GetRecordWallAsync"/>.
    /// </summary>
    Task<ClubSeasonBestDto> GetSeasonBestAsync(int resolvedClubId, string? poolType, int? season);

    /// <summary>
    /// Стена официальных рекордов клуба: строки таблицы <c>Records</c> (нац./возрастные/
    /// мастерс/мировые, импорт с isr.org.il и World Aquatics), числящиеся за этим клубом.
    ///
    /// ⚠ Связь только по НАЗВАНИЮ клуба (у Record нет ClubId/SwimmerId) — точное совпадение
    /// или название + суффикс источника; учитываются и имена склеенных дублей.
    /// <paramref name="poolType"/> — "25m"/"50m", null — оба.
    /// </summary>
    /// <summary>
    /// Сколько у клуба лучших времён страны в ПРОИЗВОЛЬНОМ окне дат — плитка Hero
    /// «season bests» за витринный сезон (<see cref="Swimm.Domain.ShowcaseSeason"/>).
    /// Отдельный метод, а не параметр к <see cref="GetSeasonBestAsync"/>: карточке нужен
    /// календарный сезон с меткой, витрине — окно и одно число.
    /// </summary>
    Task<int> GetSeasonBestCountAsync(int resolvedClubId, DateTime start, DateTime endExclusive);

    Task<ClubRecordWallDto> GetRecordWallAsync(int resolvedClubId, string? poolType);
}
