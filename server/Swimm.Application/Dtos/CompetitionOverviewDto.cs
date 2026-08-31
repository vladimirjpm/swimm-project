using System.Text.Json.Serialization;

namespace Swimm.Application.Dtos;

/// <summary>
/// Дэшборд соревнования для таба Overview (design_handoff_competition_overview, вариант 1b):
/// сводка, дни, лучший заплыв, топ-клубы (общий и по полу), топ-медалист. Всё вычислимо из
/// результатов, поэтому пустого дэшборда не бывает (пока есть хоть один результат).
/// Область = тот же источник, что у результатов: competitionId / eventId.
/// snake_case — как у CompetitionSourceDto (публичный API).
/// </summary>
public sealed class CompetitionOverviewDto
{
    [JsonPropertyName("summary")] public OverviewSummaryDto Summary { get; init; } = new();

    /// <summary>
    /// compID сайта федерации — адрес, по которому живёт стартовый протокол
    /// (<c>GET /api/start-list/{orgCompId}</c>). Источник: <c>Competition.OrgCompId</c>,
    /// а у дня многодневки, если там пусто, — <c>Competition.Event.OrgCompId</c> (штамп
    /// многодневки стоит на событии, см. <c>CompetitionIdentity</c>). <c>null</c> —
    /// соревнование завели руками, штампа нет: таб Start list клиент не показывает.
    /// </summary>
    [JsonPropertyName("org_comp_id")] public int? OrgCompId { get; init; }

    /// <summary>
    /// Все источники стартового протокола соревнования — по одному на compID федерации
    /// (таблица <c>CompetitionSources</c>). Их бывает несколько: окружные протоколы одного
    /// чемпионата лежат под разными compID, а у нас это один старт из нескольких дней.
    /// Пустой список — привязок нет; клиент тогда падает на скалярный <see cref="OrgCompId"/>,
    /// а если пуст и он — таба Start list нет. Один элемент — подтабы не рисуются.
    /// </summary>
    [JsonPropertyName("start_list_sources")]
    public IReadOnlyList<OverviewStartListSourceDto> StartListSources { get; init; } = [];

    /// <summary>
    /// Соревнование наградное (<c>Competition.IsAward</c>) — то есть места в протоколе означают
    /// награждение. У ненаградных (лиги, отборы, «результаты дня») места есть, но медалей нет,
    /// и клиент прячет всё медальное: Most decorated, медали в клубном зачёте, High Point.
    /// Расчёты при этом остаются в ответе — флаг влияет только на показ, чтобы не терять
    /// данные, если соревнованию потом проставят награждение.
    /// У многодневного события — true, если наградной хотя бы один день.
    /// </summary>
    [JsonPropertyName("has_awards")] public bool HasAwards { get; init; }
    /// <summary>Дни события (для однодневного — один элемент). Порядок — по номеру дня/дате.</summary>
    [JsonPropertyName("days")] public IReadOnlyList<OverviewDayDto> Days { get; init; } = [];
    /// <summary>Лучший заплыв соревнования — максимум FINA-очков; null, если очков нет ни у кого.</summary>
    [JsonPropertyName("best_swim")] public OverviewBestSwimDto? BestSwim { get; init; }
    /// <summary>Лучший заплыв среди мужчин / женщин (design_handoff вариант 4, ♂/♀). null — нет данных.</summary>
    [JsonPropertyName("best_swim_male")] public OverviewBestSwimDto? BestSwimMale { get; init; }
    [JsonPropertyName("best_swim_female")] public OverviewBestSwimDto? BestSwimFemale { get; init; }
    /// <summary>Клубный зачёт, топ-10 (полный — таб Clubs / /api/club-summary).</summary>
    [JsonPropertyName("top_clubs")] public IReadOnlyList<ClubSummaryDto> TopClubs { get; init; } = [];

    /// <summary>
    /// Правила клубных очков, РЕАЛЬНО применённые в этом зачёте (обычно одно; в сезонной
    /// выборке может быть несколько — у разных соревнований разные регламенты).
    /// Карточка Top clubs показывает их шкалу попапом: без неё цифра очков необъяснима.
    /// Пусто — ни к одному заплыву правило не подобралось (очков нет).
    /// </summary>
    [JsonPropertyName("club_points_rules")]
    public IReadOnlyList<OverviewPointsRuleDto> ClubPointsRules { get; init; } = [];

    /// <summary>
    /// Итог ручной проверки клубных очков (<c>official</c> | <c>accepted</c> | <c>mismatch</c>,
    /// см. PointsVerifiedKinds) — только если он одинаков у ВСЕХ соревнований выборки;
    /// иначе null. Клиент показывает бейдж на карточке Top clubs: <c>mismatch</c> означает
    /// «официальные очки расходятся с нашими, у них ошибка» — без подписи это выглядит как
    /// наш баг.
    /// </summary>
    [JsonPropertyName("club_points_verified")]
    public string? ClubPointsVerified { get; init; }

    /// <summary>
    /// Чем именно наши очки отличаются от официальных: проза на трёх языках плюс табличка
    /// «место / по регламенту / начислено». Показывается в попапе «Points system» под бейджем
    /// расхождения. null — объяснения нет. Как и бейдж, отдаётся только если одно на всю выборку.
    /// </summary>
    [JsonPropertyName("club_points_mismatch_note")]
    public CompetitionNoteDto? ClubPointsMismatchNote { get; init; }
    [JsonPropertyName("top_clubs_men")] public IReadOnlyList<ClubSummaryDto> TopClubsMen { get; init; } = [];
    [JsonPropertyName("top_clubs_women")] public IReadOnlyList<ClubSummaryDto> TopClubsWomen { get; init; } = [];
    /// <summary>
    /// Самые титулованные пловцы соревнования: личные медали ПЛЮС эстафетные (эстафетная
    /// медаль засчитывается каждому участнику по <c>RelayMembers</c>, а не владельцу строки).
    /// Порядок — сначала по золоту, затем серебру, затем бронзе (не по сумме наград).
    /// При полном равенстве набора отдаются ВСЕ — как в High Point Award.
    /// </summary>
    [JsonPropertyName("top_medalists")] public IReadOnlyList<OverviewMedalistDto> TopMedalists { get; init; } = [];
    /// <summary>То же среди мужчин / женщин (design_handoff вариант 4, ♂/♀).</summary>
    [JsonPropertyName("top_medalists_male")] public IReadOnlyList<OverviewMedalistDto> TopMedalistsMale { get; init; } = [];
    [JsonPropertyName("top_medalists_female")] public IReadOnlyList<OverviewMedalistDto> TopMedalistsFemale { get; init; } = [];
    /// <summary>High Point Award: лучший по СУММЕ очков в каждом возрасте, раздельно ♂/♀
    /// (design_handoff §High Point Award). Ничья по очкам → несколько на возраст (is_tie).
    /// Пусто, если возраст не вычислим (нет года рождения).</summary>
    [JsonPropertyName("high_point_awards")] public IReadOnlyList<OverviewHighPointDto> HighPointAwards { get; init; } = [];
    /// <summary>Новые рекорды, установленные на соревновании. v1: всегда пусто — серверного
    /// сравнения результата с Record ещё нет (у Record нет FK на Competition); контракт
    /// зарезервирован, таб Records скрывается при пустом списке.</summary>
    [JsonPropertyName("records")] public IReadOnlyList<OverviewRecordDto> Records { get; init; } = [];
}

public sealed class OverviewSummaryDto
{
    [JsonPropertyName("result_count")] public int ResultCount { get; init; }
    [JsonPropertyName("day_count")] public int DayCount { get; init; }
    [JsonPropertyName("swimmer_count")] public int SwimmerCount { get; init; }
    [JsonPropertyName("club_count")] public int ClubCount { get; init; }
}

public sealed class OverviewDayDto
{
    [JsonPropertyName("competition_id")] public int CompetitionId { get; init; }
    /// <summary>Дата дня в формате dd/MM/yyyy (как Competition.Date).</summary>
    [JsonPropertyName("date")] public string Date { get; init; } = "";
    [JsonPropertyName("day_number")] public int? DayNumber { get; init; }
    [JsonPropertyName("sub_name")] public string? SubName { get; init; }
    [JsonPropertyName("result_count")] public int ResultCount { get; init; }
}

/// <summary>
/// Один источник стартового протокола: compID федерации плюс подпись подтаба.
/// Подпись — ДАТА И НОМЕР («16/02 · #2»), а не имя протокола: имена окружных стартов у
/// федерации на иврите, а видимый UI у нас только английский. Полное имя уходит в тултип.
/// </summary>
public sealed class OverviewStartListSourceDto
{
    /// <summary>compID на isr.org.il — адрес <c>GET /api/start-list/{orgCompId}</c>.</summary>
    [JsonPropertyName("org_comp_id")] public int OrgCompId { get; init; }
    /// <summary>Наш день соревнования, к которому привязан источник.</summary>
    [JsonPropertyName("competition_id")] public int CompetitionId { get; init; }
    /// <summary>Порядковый номер источника в подтабах, с единицы.</summary>
    [JsonPropertyName("index")] public int Index { get; init; }
    /// <summary>Дата протокола, dd/MM (как в подписи подтаба). null — даты у привязки нет.</summary>
    [JsonPropertyName("date")] public string? Date { get; init; }
    /// <summary>
    /// Та же дата в ISO (yyyy-MM-dd). Нужна витрине, чтобы посчитать ДЕНЬ НЕДЕЛИ в чипе
    /// сессии («Sun 15/02 · #1», зона фильтров 5d): по <see cref="Date"/> без года его не
    /// вычислить. Формат календарный, как у ключа дня в стартовом протоколе.
    /// </summary>
    [JsonPropertyName("date_iso")] public string? DateIso { get; init; }
    /// <summary>Имя протокола у федерации — только для тултипа.</summary>
    [JsonPropertyName("source_name")] public string? SourceName { get; init; }
    /// <summary>Сколько заявок затянуто (0 — привязка есть, забора ещё не было).</summary>
    [JsonPropertyName("entry_count")] public int EntryCount { get; init; }
}

public sealed class OverviewBestSwimDto
{
    [JsonPropertyName("result_id")] public long ResultId { get; init; }
    [JsonPropertyName("swimmer_id")] public int SwimmerId { get; init; }
    [JsonPropertyName("first_name")] public string FirstName { get; init; } = "";
    [JsonPropertyName("last_name")] public string LastName { get; init; } = "";
    [JsonPropertyName("first_name_en")] public string FirstNameEn { get; init; } = "";
    [JsonPropertyName("last_name_en")] public string LastNameEn { get; init; } = "";
    [JsonPropertyName("club")] public string Club { get; init; } = "";
    [JsonPropertyName("style_name")] public string StyleName { get; init; } = "";
    [JsonPropertyName("distance")] public string Distance { get; init; } = "";
    [JsonPropertyName("gender")] public string Gender { get; init; } = "";
    [JsonPropertyName("time")] public string Time { get; init; } = "";
    /// <summary>Инвариант И11: время идёт вместе с качеством (docs/data-integrity.md).</summary>
    [JsonPropertyName("suspect_reason")] public string? SuspectReason { get; init; }
    [JsonPropertyName("international_points")] public int Points { get; init; }
    [JsonPropertyName("is_relay")] public bool IsRelay { get; init; }
    [JsonPropertyName("relay_team_name")] public string? RelayTeamName { get; init; }
    [JsonPropertyName("day_number")] public int? DayNumber { get; init; }
    [JsonPropertyName("competition_id")] public int CompetitionId { get; init; }
}

public sealed class OverviewMedalistDto
{
    [JsonPropertyName("swimmer_id")] public int SwimmerId { get; init; }
    [JsonPropertyName("first_name")] public string FirstName { get; init; } = "";
    [JsonPropertyName("last_name")] public string LastName { get; init; } = "";
    [JsonPropertyName("first_name_en")] public string FirstNameEn { get; init; } = "";
    [JsonPropertyName("last_name_en")] public string LastNameEn { get; init; } = "";
    [JsonPropertyName("club")] public string Club { get; init; } = "";
    [JsonPropertyName("gold")] public int Gold { get; init; }
    [JsonPropertyName("silver")] public int Silver { get; init; }
    [JsonPropertyName("bronze")] public int Bronze { get; init; }
    /// <summary>Сколько из медалей — эстафетные (для подписи «в т.ч. эстафеты»).</summary>
    [JsonPropertyName("relay_medals")] public int RelayMedals { get; init; }
    /// <summary>true — тот же набор медалей ещё у кого-то, награда делится.</summary>
    [JsonPropertyName("is_tie")] public bool IsTie { get; init; }
}

/// <summary>Строка шкалы правила: место → очки.</summary>
public sealed class OverviewPointsRuleEntryDto
{
    [JsonPropertyName("place")] public int Place { get; init; }
    [JsonPropertyName("points")] public int Points { get; init; }
}

/// <summary>
/// Правило начисления клубных очков в том виде, в каком его показывают пользователю:
/// версия, шкала мест, множитель эстафет. Зеркалит PointRuleClubs, но только читаемые поля.
/// </summary>
public sealed class OverviewPointsRuleDto
{
    [JsonPropertyName("version")] public string Version { get; init; } = "";
    [JsonPropertyName("description")] public string? Description { get; init; }

    /// <summary>"all" | "masters" | "non-masters".</summary>
    [JsonPropertyName("scope")] public string Scope { get; init; } = "";

    /// <summary>Дата вступления в силу, "yyyy-MM-dd".</summary>
    [JsonPropertyName("effective_from")] public string EffectiveFrom { get; init; } = "";

    /// <summary>Очки за место вне шкалы (обычно 0).</summary>
    [JsonPropertyName("default_points")] public int DefaultPoints { get; init; }

    /// <summary>Последнее место, приносящее очки; null — ограничения нет.</summary>
    [JsonPropertyName("max_scoring_place")] public int? MaxScoringPlace { get; init; }

    /// <summary>Множитель очков за эстафету (обычно 2).</summary>
    [JsonPropertyName("relay_multiplier")] public int RelayMultiplier { get; init; }

    /// <summary>Шкала «место → очки», по возрастанию места.</summary>
    [JsonPropertyName("points_by_place")]
    public IReadOnlyList<OverviewPointsRuleEntryDto> PointsByPlace { get; init; } = [];
}

/// <summary>Одна награда High Point Award: лучший по сумме очков в (возраст × пол).</summary>
public sealed class OverviewHighPointDto
{
    /// <summary>Возраст спортсмена (год соревнования − год рождения). Для masters = 0.</summary>
    [JsonPropertyName("age")] public int Age { get; init; }
    /// <summary>Masters: возрастная группа как в фильтрах ("25-29"); пусто для не-masters.</summary>
    [JsonPropertyName("age_group")] public string AgeGroup { get; init; } = "";
    /// <summary>"male" | "female".</summary>
    [JsonPropertyName("gender")] public string Gender { get; init; } = "";
    [JsonPropertyName("swimmer_id")] public int SwimmerId { get; init; }
    [JsonPropertyName("first_name")] public string FirstName { get; init; } = "";
    [JsonPropertyName("last_name")] public string LastName { get; init; } = "";
    [JsonPropertyName("first_name_en")] public string FirstNameEn { get; init; } = "";
    [JsonPropertyName("last_name_en")] public string LastNameEn { get; init; } = "";
    [JsonPropertyName("club")] public string Club { get; init; } = "";
    /// <summary>Сумма international points по личным заплывам на соревновании.</summary>
    [JsonPropertyName("points")] public int Points { get; init; }
    /// <summary>true — ничья по очкам в этом (возраст × пол): наград несколько.</summary>
    [JsonPropertyName("is_tie")] public bool IsTie { get; init; }

    /// <summary>
    /// Версия правила, по которому посчитаны очки ("2026.01-youth-11-14"), или null —
    /// правило не привязано и работает прежний расчёт по сумме international points.
    /// Клиенту нужно, чтобы подписать источник очков: «5/3/2/1 за место» и «сумма FINA» —
    /// разные величины, и без подписи цифры выглядят необъяснимо разными.
    /// </summary>
    [JsonPropertyName("rule_version")] public string? RuleVersion { get; init; }

    /// <summary>
    /// Подпись номинации, если она НЕ возрастная: возрастная группа masters ("25-29") или
    /// один кубок на пол у «бугрим» (GroupBy = none). Позволяет карточке High Point
    /// показывать произвольную номинацию, а не только возраст.
    /// </summary>
    [JsonPropertyName("group_label")] public string? GroupLabel { get; init; }

    /// <summary>
    /// Правило требует считать только финалы, но признака типа заплыва в данных ещё нет —
    /// расчёт идёт по всем заплывам (§8.B.3 плана, вариант 3). Клиенту стоит показать
    /// сноску «по всем заплывам», чтобы расхождение с официальным кубком не выглядело багом.
    /// </summary>
    [JsonPropertyName("finals_only_unavailable")] public bool FinalsOnlyUnavailable { get; init; }
}

/// <summary>Зарезервированный контракт карточки рекорда (v1 не заполняется).</summary>
public sealed class OverviewRecordDto
{
    [JsonPropertyName("kind")] public string Kind { get; init; } = "";
    [JsonPropertyName("style_name")] public string StyleName { get; init; } = "";
    [JsonPropertyName("distance")] public string Distance { get; init; } = "";
    [JsonPropertyName("gender")] public string Gender { get; init; } = "";
    [JsonPropertyName("time")] public string Time { get; init; } = "";
    [JsonPropertyName("suspect_reason")] public string? SuspectReason { get; init; }
    [JsonPropertyName("holder_name")] public string HolderName { get; init; } = "";
    /// <summary>Id пловца-держателя — для группировки рекордов по спортсмену на клиенте.</summary>
    [JsonPropertyName("swimmer_id")] public int SwimmerId { get; init; }
    /// <summary>Возрастная группа держателя ("25-29"); пусто, если нет в данных.</summary>
    [JsonPropertyName("age_group")] public string AgeGroup { get; init; } = "";
    [JsonPropertyName("club")] public string? Club { get; init; }
    [JsonPropertyName("day_number")] public int? DayNumber { get; init; }
    [JsonPropertyName("result_id")] public long? ResultId { get; init; }
}
