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
    /// Клубные рекорды: лучшее время по оси стиль×дистанция×бассейн×пол среди Results.ClubId
    /// клуба. <paramref name="poolType"/> — "25m"/"50m" фильтр, null — обе оси бассейна.
    /// </summary>
    Task<ClubRecordsDto> GetRecordsAsync(int resolvedClubId, string? poolType);
}
