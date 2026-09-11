using System.Text.Json.Serialization;

namespace Swimm.Application.Dtos;

/// <summary>
/// Доступ зрителя к группе: приватна ли она и может ли он её смотреть (участник/управляющий).
/// Не отдаётся наружу — по нему контроллеры выбирают страницу или заглушку.
/// </summary>
public sealed record HubGroupAccessDto(int Id, bool IsPrivate, bool CanView);

/// <summary>Карточка группы в публичном списке /api/hub-groups.</summary>
public sealed class HubGroupListItemDto
{
    [JsonPropertyName("slug")]
    public string Slug { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("name_en")]
    public string? NameEn { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("icon_url")]
    public string? IconUrl { get; set; }

    [JsonPropertyName("location")]
    public string? Location { get; set; }

    /// <summary>Alpha-3 код страны группы (ISR…), null — не задана.</summary>
    [JsonPropertyName("country")]
    public string? Country { get; set; }

    [JsonPropertyName("club_name")]
    public string? ClubName { get; set; }

    /// <summary>Официальная группа клуба (одобрена админом) — не путать с составом-watchlist.</summary>
    [JsonPropertyName("is_official")]
    public bool IsOfficial { get; set; }

    [JsonPropertyName("member_count")]
    public int MemberCount { get; set; }
}

/// <summary>Публичная ссылка группы (whatsapp/telegram/instagram/site).</summary>
public sealed class HubGroupPublicLinkDto
{
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "";

    [JsonPropertyName("url")]
    public string Url { get; set; } = "";
}

/// <summary>Участник группы на публичной странице.</summary>
public sealed class HubGroupPublicMemberDto
{
    [JsonPropertyName("swimmer_id")]
    public int SwimmerId { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("name_en")]
    public string NameEn { get; set; } = "";

    [JsonPropertyName("birth_year")]
    public int BirthYear { get; set; }

    [JsonPropertyName("club_name")]
    public string? ClubName { get; set; }

    /// <summary>member | captain | coach</summary>
    [JsonPropertyName("role")]
    public string Role { get; set; } = "member";
}

/// <summary>«Рекорд группы» — лучшее время участников по оси стиль+дистанция+бассейн.</summary>
public sealed class HubGroupBestDto
{
    [JsonPropertyName("style_name")]
    public string StyleName { get; set; } = "";

    [JsonPropertyName("distance")]
    public string Distance { get; set; } = "";

    [JsonPropertyName("pool_type")]
    public string? PoolType { get; set; }

    [JsonPropertyName("gender")]
    public string Gender { get; set; } = "";

    [JsonPropertyName("time_original")]
    public string TimeOriginal { get; set; } = "";

    /// <summary>Инвариант И11: DTO со временем несёт и качество. null — заплыв в порядке.</summary>
    [JsonPropertyName("suspect_reason")]
    public string? SuspectReason { get; set; }

    [JsonPropertyName("time_millisecond")]
    public int? TimeMillisecond { get; set; }

    [JsonPropertyName("swimmer_id")]
    public int SwimmerId { get; set; }

    [JsonPropertyName("swimmer_name")]
    public string SwimmerName { get; set; } = "";

    [JsonPropertyName("swimmer_name_en")]
    public string SwimmerNameEn { get; set; } = "";

    [JsonPropertyName("competition_name")]
    public string CompetitionName { get; set; } = "";

    /// <summary>dd/MM/yyyy — формат дат соревнований во всех публичных ответах.</summary>
    [JsonPropertyName("date")]
    public string Date { get; set; } = "";

    [JsonPropertyName("points")]
    public int Points { get; set; }
}

/// <summary>Строка сезонного зачёта группы — рейтинг участника по клубным очкам.</summary>
public sealed class HubGroupStandingDto
{
    [JsonPropertyName("swimmer_id")]
    public int SwimmerId { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("name_en")]
    public string NameEn { get; set; } = "";

    /// <summary>member | captain | coach</summary>
    [JsonPropertyName("role")]
    public string Role { get; set; } = "member";

    /// <summary>Кол-во зачтённых заплывов за сезон (без эстафет).</summary>
    [JsonPropertyName("swims")]
    public int Swims { get; set; }

    [JsonPropertyName("golds")]
    public int Golds { get; set; }

    [JsonPropertyName("silvers")]
    public int Silvers { get; set; }

    [JsonPropertyName("bronzes")]
    public int Bronzes { get; set; }

    /// <summary>Сумма клубных очков за сезон по правилам ClubPointsRule.</summary>
    [JsonPropertyName("club_points")]
    public int ClubPoints { get; set; }

    /// <summary>Лучший FINA-балл (InternationalPoints) за сезон, 0 если нет.</summary>
    [JsonPropertyName("best_fina")]
    public int BestFina { get; set; }
}

/// <summary>
/// Карточка ленты хайлайтов шапки группы (design_handoff_group_header).
/// Дискриминированный union по <see cref="Type"/>: record | medals | video | photo —
/// у каждого варианта заполнен свой поднабор полей, остальные null и не сериализуются.
/// Состав и порядок ленты задаёт сервер (HubGroupHighlightsBuilder); новые типы
/// добавляются новым значением Type без изменения шапки на клиенте.
/// </summary>
public sealed class HubGroupHighlightDto
{
    /// <summary>record | medals | video | photo</summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    /// <summary>Куда ведёт клик по карточке (относительный или внешний URL).</summary>
    [JsonPropertyName("url")]
    public string Url { get; set; } = "";

    // --- record ---
    [JsonPropertyName("badge")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Badge { get; set; }

    [JsonPropertyName("title")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Title { get; set; }

    [JsonPropertyName("detail")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Detail { get; set; }

    // --- medals ---
    [JsonPropertyName("place")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Place { get; set; }

    [JsonPropertyName("place_label")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PlaceLabel { get; set; }

    [JsonPropertyName("gold")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Gold { get; set; }

    [JsonPropertyName("silver")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Silver { get; set; }

    [JsonPropertyName("bronze")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Bronze { get; set; }

    // --- video / photo ---
    [JsonPropertyName("label")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Label { get; set; }

    /// <summary>Длительность видео (mm:ss); в БД её нет, поэтому обычно null.</summary>
    [JsonPropertyName("duration")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Duration { get; set; }

    [JsonPropertyName("thumb_url")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ThumbUrl { get; set; }

    /// <summary>«+N» — сколько ещё фото в галерее сверх превью.</summary>
    [JsonPropertyName("extra")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Extra { get; set; }
}

/// <summary>
/// Полная публичная страница группы (/api/hub-groups/{slug}).
/// Тот же контракт отдаёт /api/hub-groups/favorites — виртуальная группа
/// «Моё избранное» поверх Sys_UserFavorites (is_virtual=true, slug="favorites").
/// </summary>
public sealed class HubGroupDetailsDto
{
    /// <summary>Числовой id группы — для действий по id (самозапись). 0 для виртуального «избранного».</summary>
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("slug")]
    public string Slug { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("name_en")]
    public string? NameEn { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("icon_url")]
    public string? IconUrl { get; set; }

    [JsonPropertyName("cover_image_url")]
    public string? CoverImageUrl { get; set; }

    /// <summary>
    /// Фото шапки — УЖЕ РАЗРЕШЁННОЕ сервером: hero.mediaId (взять из медиа-ленты) →
    /// CoverImageUrl → null. Клиент про формы указателя не знает, у него одно поле.
    /// null — картинки нет, страница рисует заглушку.
    /// </summary>
    [JsonPropertyName("hero_image_url")]
    public string? HeroImageUrl { get; set; }

    /// <summary>Показывать блок фото шапки (настройка hero.show). false — колонка схлопнута.</summary>
    [JsonPropertyName("show_hero_image")]
    public bool ShowHeroImage { get; set; } = true;

    /// <summary>Id медиа, помеченного как фото шапки (hero.mediaId). Нужен табу Media,
    /// чтобы отметить активную кнопку «Сделать фото шапки». null — фото берётся из URL.</summary>
    [JsonPropertyName("hero_media_id")]
    public int? HeroMediaId { get; set; }

    [JsonPropertyName("location")]
    public string? Location { get; set; }

    /// <summary>Alpha-3 код страны группы (ISR…), null — не задана.</summary>
    [JsonPropertyName("country")]
    public string? Country { get; set; }

    [JsonPropertyName("club_name")]
    public string? ClubName { get; set; }

    /// <summary>Официальная группа клуба (одобрена админом) — не путать с составом-watchlist.</summary>
    [JsonPropertyName("is_official")]
    public bool IsOfficial { get; set; }

    /// <summary>open | approval — чтобы кнопка вступления показывала «Подать заявку».</summary>
    [JsonPropertyName("join_policy")]
    public string JoinPolicy { get; set; } = "open";

    /// <summary>Группа только для участников (HubGroupVisibilityRules). Участник видит её целиком.</summary>
    [JsonPropertyName("is_private")]
    public bool IsPrivate { get; set; }

    /// <summary>
    /// Заглушка для НЕ-участника приватной группы: только имя, иконка и как вступить — ни
    /// состава, ни результатов, ни медиа. Вступление в приватную — всегда заявкой.
    /// </summary>
    [JsonPropertyName("members_only")]
    public bool MembersOnly { get; set; }

    /// <summary>Клуб, на который подписана группа (состав из клуба); null — подписки нет.</summary>
    [JsonPropertyName("followed_club_id")]
    public int? FollowedClubId { get; set; }

    [JsonPropertyName("followed_club_name")]
    public string? FollowedClubName { get; set; }

    /// <summary>
    /// Официальная группа клуба подписки, если это не эта группа (П4): копию клуба открыли по
    /// ссылке — шапка показывает, где «лицо клуба». null — официальной нет или это она сама.
    /// </summary>
    [JsonPropertyName("official_group_slug")]
    public string? OfficialGroupSlug { get; set; }

    [JsonPropertyName("official_group_name")]
    public string? OfficialGroupName { get; set; }

    [JsonPropertyName("links")]
    public List<HubGroupPublicLinkDto> Links { get; set; } = [];

    /// <summary>true у виртуальной группы «Моё избранное».</summary>
    [JsonPropertyName("is_virtual")]
    public bool IsVirtual { get; set; }

    [JsonPropertyName("members")]
    public List<HubGroupPublicMemberDto> Members { get; set; } = [];

    /// <summary>Последние заплывы участников (свежие сверху).</summary>
    [JsonPropertyName("recent_results")]
    public List<ResultDto> RecentResults { get; set; } = [];

    /// <summary>Рекорды группы: лучшее время по каждой оси стиль+дистанция+бассейн.</summary>
    [JsonPropertyName("bests")]
    public List<HubGroupBestDto> Bests { get; set; } = [];

    /// <summary>Метка сезона зачёта, напр. "2025/26".</summary>
    [JsonPropertyName("season_label")]
    public string SeasonLabel { get; set; } = "";

    /// <summary>Сезонный зачёт участников по клубным очкам (desc по очкам, затем заплывам).</summary>
    [JsonPropertyName("standings")]
    public List<HubGroupStandingDto> Standings { get; set; } = [];

    /// <summary>Публичная галерея группы (HubGroupMedia.TrainingId == null).</summary>
    [JsonPropertyName("gallery")]
    public List<HubGroupMediaDto> Gallery { get; set; } = [];

    /// <summary>
    /// Лента хайлайтов шапки (record/medals/video/photo) — собирается сервером из
    /// bests/standings/gallery (HubGroupHighlightsBuilder). Пустая лента = модуль скрыт.
    /// </summary>
    [JsonPropertyName("highlights")]
    public List<HubGroupHighlightDto> Highlights { get; set; } = [];

    /// <summary>Регулярное расписание группы. null — не заведено, слот шапки скрыт.</summary>
    [JsonPropertyName("training_schedule")]
    public GroupTrainingScheduleDto? TrainingSchedule { get; set; }

    /// <summary>
    /// Ближайшее занятие по расписанию. Считает СЕРВЕР (в поясе Израиля) — чтобы «завтра»
    /// не зависело от часов зрителя и чтобы логику покрыли тесты. null — расписания нет.
    /// </summary>
    [JsonPropertyName("next_training")]
    public NextTrainingDto? NextTraining { get; set; }
}

/// <summary>Расписание как оно есть — для показа строкой и для формы редактирования.</summary>
public sealed class GroupTrainingScheduleDto
{
    [JsonPropertyName("slots")]
    public List<GroupTrainingSlotDto> Slots { get; set; } = [];

    [JsonPropertyName("place")]
    public string? Place { get; set; }

    [JsonPropertyName("pool_type")]
    public string? PoolType { get; set; }

    [JsonPropertyName("note")]
    public string? Note { get; set; }
}

/// <summary>Занятие недели: день ISO (1 = Mon … 7 = Sun) и часы по стенным часам бассейна.</summary>
public sealed class GroupTrainingSlotDto
{
    [JsonPropertyName("day")]
    public int Day { get; set; }

    [JsonPropertyName("start")]
    public string Start { get; set; } = "";

    [JsonPropertyName("end")]
    public string? End { get; set; }
}

/// <summary>Ближайшее занятие: дата ISO + часы; место дублируется, чтобы слот был самодостаточен.</summary>
public sealed class NextTrainingDto
{
    /// <summary>yyyy-MM-dd в местном времени Израиля.</summary>
    [JsonPropertyName("date")]
    public string Date { get; set; } = "";

    [JsonPropertyName("start")]
    public string Start { get; set; } = "";

    [JsonPropertyName("end")]
    public string? End { get; set; }

    [JsonPropertyName("place")]
    public string? Place { get; set; }

    [JsonPropertyName("pool_type")]
    public string? PoolType { get; set; }
}
