using System.Text.Json.Serialization;

namespace Swimm.Application.Dtos;

/// <summary>
/// DTO результата для JSON API.
/// Содержит все денормализованные данные без дополнительных JOIN.
/// </summary>
public class ResultDto
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("country")]
    public string Country { get; set; } = string.Empty;

    [JsonPropertyName("competition")]
    public string CompetitionName { get; set; } = string.Empty;

    [JsonPropertyName("is_masters")]
    public bool IsMasters { get; set; }

    [JsonPropertyName("is_award")]
    public bool IsAward { get; set; }

    /// <summary>Per-competition: показывать объединённую таблицу всех результатов.</summary>
    [JsonPropertyName("show_combine_all_results")]
    public bool ShowCombineAllResults { get; set; }

    [JsonPropertyName("age_group")]
    public string AgeGroup { get; set; } = string.Empty;

    /// <summary>Дата в формате dd/MM/yyyy (берётся из Competition.Date как есть).</summary>
    [JsonPropertyName("date")]
    public string Date { get; set; } = string.Empty;

    // ── Многодневные соревнования ──
    // Для дней события CompetitionName уже = общее имя события; sub_name несёт заголовок дня.
    /// <summary>Id события, если соревнование — день многодневного. null для однодневных.</summary>
    [JsonPropertyName("event_id")]
    public int? EventId { get; set; }

    /// <summary>Общее имя события (для дней многодневного). null для однодневных.</summary>
    [JsonPropertyName("event_name")]
    public string? EventName { get; set; }

    /// <summary>Номер дня внутри события (1..N). null для однодневных.</summary>
    [JsonPropertyName("day_number")]
    public int? DayNumber { get; set; }

    /// <summary>Оригинальный заголовок соревнования этого дня. null для однодневных.</summary>
    [JsonPropertyName("sub_name")]
    public string? SubName { get; set; }

    /// <summary>
    /// Отображаемое название события (напр. "200 חופשי - בנות 12").
    /// ОТЛОЖЕНО: в БД не хранится (импорт его теряет) — пока отдаётся пустым.
    /// </summary>
    [JsonPropertyName("event")]
    public string Event { get; set; } = string.Empty;

    [JsonPropertyName("event_style_name")]
    public string StyleName { get; set; } = string.Empty;

    [JsonPropertyName("event_style_len")]
    public string Distance { get; set; } = string.Empty;

    [JsonPropertyName("event_style_gender")]
    public string Gender { get; set; } = string.Empty;

    [JsonPropertyName("event_style_age")]
    public string EventStyleAge { get; set; } = string.Empty;

    /// <summary>
    /// Категория заплыва из протокола: open / para / mix / возрастная («17», «25-29»).
    /// null — данные импортированы до появления поля. В отличие от event_style_age НЕ
    /// производна от года рождения: в одном открытом заплыве плывут разные возрасты.
    /// </summary>
    [JsonPropertyName("event_category")]
    public string? EventCategory { get; set; }

    /// <summary>
    /// Тип заплыва: prelim / final; null — единственный заплыв дисциплины за день (timed
    /// final) либо данные без признака. Место prelim-заплыва — ранжир сессии, не награда:
    /// клубные очки за него не начисляются (ApplyClubPoints), медали не считаются.
    /// </summary>
    [JsonPropertyName("heat_type")]
    public string? HeatType { get; set; }

    /// <summary>
    /// Раунд зачёта из источника: <c>timed-final</c> (утренний зачёт возрастных групп),
    /// <c>final</c> (финал первенства), <c>prelim</c>; null — источник раундов не различает.
    /// Нужен экрану: у чемпионата «мокдамот и финал» один пловец законно занимает ПЕРВОЕ
    /// место дважды — в утреннем и вечернем зачёте, — и без метки это выглядит багом
    /// (И13, docs/data-integrity.md §10).
    /// </summary>
    [JsonPropertyName("round")]
    public string? Round { get; set; }

    /// <summary>
    /// Заплыв помечен как недостоверный: ошибка САМОГО протокола (docs/data-integrity.md).
    /// Строка остаётся в результатах — мы не переписываем протокол, — но клиент обязан
    /// показать это глазом, иначе бессмыслица вроде 200 вольным за 1:53 у 13-летнего
    /// выглядит как достижение и получает бейдж рекорда.
    /// null — строка в порядке.
    /// </summary>
    [JsonPropertyName("suspect_reason")]
    public string? SuspectReason { get; set; }

    [JsonPropertyName("pool_type")]
    public string PoolType { get; set; } = string.Empty;

    [JsonPropertyName("position")]
    public int? Position { get; set; }

    [JsonPropertyName("position_age_group")]
    public int? PositionAgeGroup { get; set; }

    /// <summary>Место в объединённом зачёте дисциплины по всему событию («Combine All Results»).
    /// Заполнено только у соревнований с ShowCombineAllResults; null — режим не применим или
    /// заплыв незачтён. Клиенту нужны ОБА места: объединённое — крупной медалью, протокольное
    /// (<see cref="Position"/>) — маленьким бейджем внахлёст (results-table-desktop.tsx).</summary>
    [JsonPropertyName("combined_place")]
    public int? CombinedPlace { get; set; }

    /// <summary>
    /// Клубные очки за этот заплыв по правилу СОРЕВНОВАНИЯ (Э6). Считает сервер — клиенту
    /// больше не нужно ни знать шкалу, ни угадывать правило по дате: он не видит привязки
    /// соревнования к правилу и на manual-правилах расходился с зачётом (см.
    /// docs/competition-overview-cards.md, раздел Top clubs).
    /// Учитывает множитель эстафеты; TimeFail (DSQ) даёт 0.
    /// </summary>
    [JsonPropertyName("club_points")]
    public int ClubPoints { get; set; }

    /// <summary>
    /// Клубные очки по ОБЪЕДИНЁННОМУ месту дисциплины (тоггл «Combine All Results»).
    /// null — соревнование объединённый зачёт не считает (нет <see cref="CombinedPlace"/>).
    /// Тоггл переключает, какое поле показывать, а не запускает пересчёт на клиенте.
    /// </summary>
    [JsonPropertyName("combined_club_points")]
    public int? CombinedClubPoints { get; set; }

    /// <summary>Этот заплыв — лучший у пловца в дисциплине за событие.</summary>
    [JsonPropertyName("is_best_result")]
    public bool? IsBestResult { get; set; }

    /// <summary>Лучшее время пловца в этой дисциплине за событие, мс.</summary>
    [JsonPropertyName("best_time_ms")]
    public int? BestTimeMs { get; set; }

    [JsonPropertyName("heat")]
    public int Heat { get; set; }

    [JsonPropertyName("lane")]
    public int Lane { get; set; }

    [JsonPropertyName("swimmer_id")]
    public int SwimmerId { get; set; }

    [JsonPropertyName("last_name")]
    public string LastName { get; set; } = string.Empty;

    [JsonPropertyName("first_name")]
    public string FirstName { get; set; } = string.Empty;

    [JsonPropertyName("last_name_en")]
    public string LastNameEn { get; set; } = string.Empty;

    [JsonPropertyName("first_name_en")]
    public string FirstNameEn { get; set; } = string.Empty;

    [JsonPropertyName("birth_year")]
    public int BirthYear { get; set; }

    /// <summary>
    /// Id клуба. Нужен, чтобы клуб был ССЫЛКОЙ на свою страницу (<c>/clubs/{id}</c>), а не
    /// строкой: имена не уникальны, а после merge дублей строки-имена остаются одинаковыми.
    /// Читается из готового FK <c>ResultRecord.ClubId</c> — лишнего JOIN не добавляет.
    /// </summary>
    [JsonPropertyName("club_id")]
    public int ClubId { get; set; }

    [JsonPropertyName("club")]
    public string ClubName { get; set; } = string.Empty;

    [JsonPropertyName("club_en")]
    public string ClubNameEn { get; set; } = string.Empty;

    [JsonPropertyName("time_ms")]
    public int? TimeMillisecond { get; set; }

    [JsonPropertyName("time")]
    public string TimeOriginal { get; set; } = string.Empty;

    [JsonPropertyName("time_split")]
    public string TimeSplit { get; set; } = string.Empty;

    [JsonPropertyName("time_fail")]
    public bool TimeFail { get; set; }

    [JsonPropertyName("time_fail_note")]
    public string? TimeFailNote { get; set; }

    [JsonPropertyName("international_points")]
    public int InternationalPoints { get; set; }

    [JsonPropertyName("note")]
    public string? Note { get; set; }

    [JsonPropertyName("is_relay")]
    public bool IsRelay { get; set; }

    /// <summary>
    /// Правило клубных очков, привязанное к соревнованию (null — подбор по дате и типу).
    /// Наружу не отдаётся: нужно только серверу, чтобы посчитать <see cref="ClubPoints"/>
    /// после материализации страницы — правило резолвится в памяти, как в клубном зачёте.
    /// </summary>
    [JsonIgnore]
    public int? PointRuleClubsId { get; set; }

    [JsonPropertyName("relay_team_name")]
    public string? RelayTeamName { get; set; }

    [JsonPropertyName("relay_swimmers_name")]
    public string? RelaySwimmersName { get; set; }

    /// <summary>Состав ног эстафеты (RelayMembers) — клиент матчит пловца к эстафете по нему,
    /// а не по владельцу строки (docs/relays.md). null для личных заплывов.</summary>
    [JsonPropertyName("member_swimmer_ids")]
    public List<int>? MemberSwimmerIds { get; set; }

    // relay_swimmers[] (структурный состав) ОТЛОЖЕН: в БД хранится только Relay.SwimmersName
    // (строка), структурированного массива нет — восстановить нельзя.

    [JsonPropertyName("gallery")]
    public List<GalleryItemDto>? Gallery { get; set; }
}

/// <summary>Элемент галереи для JSON API (совпадает с клиентским GalleryItem).</summary>
public class GalleryItemDto
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("sourceType")]
    public string? SourceType { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("code")]
    public string? Code { get; set; }
}
