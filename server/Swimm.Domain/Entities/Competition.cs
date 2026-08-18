using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Swimm.Domain.Entities;

/// <summary>
/// Справочник соревнований.
/// </summary>
public class Competition
{
  [Key]
  [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
  public int Id { get; set; }

  /// <summary>Название соревнования</summary>
  [MaxLength(300)]
  public string Name { get; set; } = string.Empty;

  /// <summary>Страна проведения — FK на справочник Countries (как у Swimmer/Club/HubGroup).
  /// null — страна не задана (старые/ручные соревнования).</summary>
  public int? CountryId { get; set; }

  [ForeignKey(nameof(CountryId))]
  public Country? Country { get; set; }

  /// <summary>Дата соревнования в формате dd/MM/yyyy</summary>
  [MaxLength(20)]
  public string Date { get; set; } = string.Empty;

  /// <summary>Тип бассейна: 25m / 50m</summary>
  [MaxLength(5)]
  public string PoolType { get; set; } = string.Empty;

  public bool IsMasters { get; set; }

  public bool IsAward { get; set; }

  /// <summary>
  /// Чемпионат Израиля (אליפות ישראל). Ставится вручную на /Admin/Competitions/Edit;
  /// при добавлении колонки проставлен бэкфиллом по названию. Даёт 🏆 в списке админки,
  /// в плитке соревнования и фильтр «только чемпионаты» в селекторе на клиенте.
  /// </summary>
  public bool IsChampionship { get; set; }

  /// <summary>
  /// Признак: отображать объединённую таблицу всех результатов (без разбивки по полу/возрасту).
  /// </summary>
  public bool ShowCombineAllResults { get; set; }

  // ── Многодневные соревнования ──────────────────────────────────────────────

  /// <summary>
  /// Ссылка на событие, если этот день — часть многодневного соревнования.
  /// null — обычное однодневное соревнование.
  /// </summary>
  public int? EventId { get; set; }

  [ForeignKey(nameof(EventId))]
  public CompetitionEvent? Event { get; set; }

  /// <summary>Порядковый номер дня внутри события (1..N). null для однодневных.</summary>
  public int? DayNumber { get; set; }

  /// <summary>
  /// Оригинальный заголовок соревнования из файла этого дня.
  /// Для дней события <see cref="Name"/> = общее имя события, а специфичный заголовок дня тут.
  /// </summary>
  [MaxLength(300)]
  public string? SubName { get; set; }

  // ── Внешний ID соревнования у организатора (loglig.com и т.п.) ─────────────

  /// <summary>
  /// ID соревнования во внешней системе организатора (например loglig.com competitionId).
  /// null — у старых/ручных соревнований этого ID пока нет.
  /// Связь с <see cref="CompetitionResultUrl"/> идёт по этому полю (не по <see cref="Id"/>);
  /// FK-констрейнт создан raw SQL в миграции — не смоделирован в EF (alternate key требует NOT NULL).
  /// </summary>
  public int? OrgCompId { get; set; }

  /// <summary>
  /// Вид спорта: swimming | artistic | other (см. <c>Disciplines</c> в Application).
  /// У нас пока только плавание, но федерация проводит и артистическое — признак нужен,
  /// чтобы витрины могли его отделить, если такие протоколы когда-нибудь импортируют.
  /// </summary>
  [MaxLength(20)]
  public string Discipline { get; set; } = "swimming";

  // ── Проверка качества результатов ──────────────────────────────────────────

  /// <summary>
  /// Когда по этому дню в последний раз гоняли проверку качества («⚑ Качество»).
  /// null — не гоняли ни разу, и это НЕ то же самое, что «ошибок нет»: пустой список
  /// без отметки о прогоне читался бы как «всё чисто», хотя никто не смотрел.
  /// Ставится <c>SuspectResultService.ScanAsync</c> по всему скоупу прогона.
  /// </summary>
  public DateTime? QualityScannedAt { get; set; }

  // ── Правила очков (Э0 — только привязка, расчёт не читает) ─────────────────

  /// <summary>Правило клубных очков, привязанное вручную. null — подбор по дате и scope.</summary>
  public int? PointRuleClubsId { get; set; }

  [ForeignKey(nameof(PointRuleClubsId))]
  public PointRuleClubs? PointRuleClubs { get; set; }

  /// <summary>
  /// Ручное переопределение роли соревнования в КЛУБНОМ ЗАЧЁТЕ сезона:
  /// <c>winter</c> | <c>summer</c> | <c>openwater</c> | <c>none</c> (явно исключить).
  /// null — обычный случай: роль выводится сама из <see cref="IsChampionship"/> +
  /// <see cref="PoolType"/> (25m → зимний ❄, 50m → летний ☀).
  ///
  /// Правило проверено на всех 23 чемпионатах в БД (2026-08-01): 12 в 25м — январь/февраль,
  /// 11 в 50м — июль/август, исключений нет. Поле заведено под будущие исключения — прежде
  /// всего открытую воду, где бассейна нет вовсе. Подробности — docs/plans/club-page-model.md §2.1.
  /// </summary>
  [MaxLength(20)]
  public string? StandingKindOverride { get; set; }

  /// <summary>
  /// Клубный зачёт на этом соревновании НЕ ВЕДЁТСЯ — очки клубам тут не считает никто
  /// (лиги, товарищеские старты, отборочные). Отличает «правило не нужно» от «правило
  /// забыли привязать»: в БД оба выглядели как <see cref="PointRuleClubsId"/> = null, и
  /// проверка реестра не могла их различить (решение Р19, docs/data-integrity.md).
  ///
  /// Пометка, а не выключатель: расчёт очков она пока НЕ трогает — это отдельное решение,
  /// меняющее цифры на публичных страницах.
  /// </summary>
  public bool ClubPointsDisabled { get; set; }

  /// <summary>
  /// Опубликован ли у соревнования ОФИЦИАЛЬНЫЙ клубный зачёт — тот, что на loglig показывает
  /// кнопка «דירוג מועדונים». Проставляется автоматически при затягивании соревнования
  /// (см. <c>IOfficialClubStandingService</c>).
  ///
  /// Три состояния, и <c>null</c> здесь значимый: <b>не проверяли</b> (нет LogligId, сайт был
  /// недоступен) — это не то же самое, что «зачёта нет». <c>false</c> — проверили, зачёта нет:
  /// наши клубные очки сверять не с чем (лиги, мокдамот, региональные этапы — таких
  /// большинство). <c>true</c> — официальные очки есть, расхождение с ними значимо.
  ///
  /// Кнопка на странице loglig НЕ признак: она в шаблоне у всех соревнований
  /// (docs/points-rules-per-competition-plan.md §10.1).
  /// </summary>
  public bool? HasOfficialClubStanding { get; set; }

  /// <summary>Правило очков пловца (High Point). null — legacy-расчёт по FINA.</summary>
  public int? PointRuleSwimmersId { get; set; }

  [ForeignKey(nameof(PointRuleSwimmersId))]
  public PointRuleSwimmers? PointRuleSwimmers { get; set; }

  // ── Ручная сверка очков (админская отметка, на публику не влияет) ──────────

  /// <summary>
  /// Когда админ вручную проверил КЛУБНЫЙ зачёт этого соревнования. null — не проверялся.
  /// Отметка ставится на /Admin/PointsRules, нужна чтобы не перепроверять дважды и
  /// на расчёт очков не влияет. Чем именно кончилась проверка — в
  /// <see cref="ClubPointsVerifiedKind"/>.
  /// </summary>
  public DateTime? ClubPointsVerifiedAt { get; set; }

  /// <summary>Кто поставил отметку клубного зачёта (логин админа).</summary>
  [MaxLength(200)]
  public string? ClubPointsVerifiedBy { get; set; }

  /// <summary>Итог проверки клубного зачёта: см. <see cref="PointsVerifiedKinds"/>.
  /// null — не проверялся.</summary>
  [MaxLength(20)]
  public string? ClubPointsVerifiedKind { get; set; }

  /// <summary>
  /// Чем именно наши клубные очки отличаются от официальных — текст ДЛЯ ЧИТАТЕЛЯ САЙТА,
  /// поэтому по-английски (правило «UI только English»).
  ///
  /// Живёт рядом с <see cref="ClubPointsVerifiedKind"/>, потому что смысл имеет только
  /// вместе с ним: бейдж «Differs from official» без объяснения — это утверждение без
  /// доказательства, а объяснение у каждого соревнования своё (у 1501 — хвост шкалы,
  /// у следующего будет что-то другое). Показывается в попапе «Points system».
  /// </summary>
  [MaxLength(2000)]
  public string? ClubPointsVerifiedNote { get; set; }

  /// <summary>Когда вручную проверен HIGH POINT этого соревнования. См. <see cref="ClubPointsVerifiedAt"/>.</summary>
  public DateTime? SwimmersPointsVerifiedAt { get; set; }

  /// <summary>Кто поставил отметку High Point (логин админа).</summary>
  [MaxLength(200)]
  public string? SwimmersPointsVerifiedBy { get; set; }

  /// <summary>Итог проверки High Point: см. <see cref="PointsVerifiedKinds"/>. null — не проверялся.</summary>
  [MaxLength(20)]
  public string? SwimmersPointsVerifiedKind { get; set; }
}

/// <summary>
/// Итог ручной проверки очков соревнования (<see cref="Competition.ClubPointsVerifiedKind"/>).
/// Состояния взаимоисключающие: «сверено с официальным протоколом» и «официальных очков нет,
/// принято как верное после проверки» — разные степени доверия к цифре.
/// </summary>
public static class PointsVerifiedKinds
{
    /// <summary>Сверено с официальным протоколом организатора — цифры совпали.</summary>
    public const string Official = "official";

    /// <summary>Официальных очков не публиковали; проверено вручную и принято как верное.</summary>
    public const string Accepted = "accepted";

    /// <summary>Официальные очки есть, но в них ошибка: сверено вручную, верны наши.</summary>
    public const string Mismatch = "mismatch";

    public static bool IsKnown(string? kind) => kind is Official or Accepted or Mismatch;
}
