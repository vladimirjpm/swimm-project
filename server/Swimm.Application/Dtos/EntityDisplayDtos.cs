namespace Swimm.Application.Dtos;

/// <summary>
/// Настройки отображения страницы коллектива — вход эндпоинта
/// <c>PUT /api/display-settings/{type}/{id}</c>.
///
/// Семантика — ПОЛНАЯ ЗАМЕНА, а не патч: форма на странице маленькая и всегда предзаполнена
/// текущим состоянием, поэтому три необязательных поля с тристейтом («не трогать» / «задать» /
/// «очистить») стоили бы дороже, чем присылать её целиком.
///
/// Клуб и группа ходят одним контрактом: это два вида одного — коллектив пловцов
/// (docs/plans/entity-page-shell-plan.md §3.10).
/// </summary>
public sealed class EntityDisplayInputDto
{
    /// <summary>Показывать блок фото шапки. false — правая колонка схлопнута.</summary>
    public bool ShowHeroImage { get; set; } = true;

    /// <summary>
    /// «Взять фото из медиа-ленты»: id строки в ленте (`gallery` публичного ответа).
    /// null — брать <see cref="CoverImageUrl"/>.
    /// </summary>
    public int? HeroMediaId { get; set; }

    /// <summary>URL обложки. null или пусто — очистить.</summary>
    public string? CoverImageUrl { get; set; }
}
