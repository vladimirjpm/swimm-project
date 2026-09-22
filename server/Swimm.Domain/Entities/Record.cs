using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Swimm.Domain.Entities;

/// <summary>
/// Рекорд по плаванию (замена клиентских normative-records/age-records/masters-records.js).
/// Три независимые оси вместо плоского scope — расширяется на любые страны/континенты/категории
/// без изменения схемы:
///   1) территория: RegionType + RegionCode (чей рекорд — мира/Европы/страны);
///   2) категория спортсменов: Category + AgeKey (open/age/junior/masters);
///   3) дисциплина: Gender + PoolType + Style + Distance.
/// API и кэш режутся по региону: records:{region}:{category}.
/// </summary>
public class Record
{
    /// <summary>RegionType: world — RegionCode пуст; continent — код континента (EU/AS/…);
    /// country — ISO-код страны (ISR/USA/…).</summary>
    public static readonly IReadOnlySet<string> RegionTypes = new HashSet<string>
    {
        "world", "continent", "country"
    };

    /// <summary>Категории: open — абсолютный рекорд; age — по одному возрасту ("10"…"18");
    /// junior — юниорские (формат AgeKey определится с данными, напр. "U17");
    /// masters — по возрастным группам ("25-29"…"80-84").</summary>
    public static readonly IReadOnlySet<string> Categories = new HashSet<string>
    {
        "open", "age", "junior", "masters"
    };

    /// <summary>Пол дисциплины: male | female | mixed. <c>mixed</c> — смешанная эстафета (Э2 плана
    /// docs/plans/records-relays-plan.md, 22.09.2026), бывает только при эстафетной дистанции.
    /// ⚠ Не путать с <c>none</c> в <c>Results</c>: там это «пол НЕ ИЗВЕСТЕН», в справочник не пишется.</summary>
    public static readonly IReadOnlySet<string> Genders = new HashSet<string>
    {
        "male", "female", "mixed"
    };

    /// <summary>Эстафету узнают по дистанции: «4X100m», «4x50» — отдельных эстафетных стилей нет.</summary>
    public static bool IsRelayDistance(string? distance) =>
        distance is { Length: > 2 } && distance[0] == '4' && distance[1] is 'X' or 'x';

    /// <summary>null — пол годится для дисциплины; иначе текст ошибки (админка, API).</summary>
    public static string? ValidateGender(string? gender, string? distance)
    {
        if (gender is null || !Genders.Contains(gender))
            return "gender должен быть male, female или mixed";
        if (gender == "mixed" && !IsRelayDistance(distance))
            return "gender=mixed бывает только у эстафеты (дистанция 4X…)";
        return null;
    }

    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    // ── Ось 1: территория ────────────────────────────────────────────────────

    /// <summary>world | continent | country.</summary>
    [Required, MaxLength(10)]
    public string RegionType { get; set; } = string.Empty;

    /// <summary>Пусто для world; EU/AS/… для continent; ISO-код (ISR, USA, …) для country.
    /// NOT NULL (пустая строка) — ради честного составного уникального индекса.</summary>
    [Required, MaxLength(10)]
    public string RegionCode { get; set; } = string.Empty;

    // ── Ось 2: категория спортсменов ─────────────────────────────────────────

    /// <summary>open | age | junior | masters.</summary>
    [Required, MaxLength(10)]
    public string Category { get; set; } = string.Empty;

    /// <summary>Возрастной ключ: "" для open; "10"…"18" для age; "25-29"…"80-84" для masters;
    /// формат юниоров — по данным (напр. "U17"). NOT NULL ради уникального индекса.</summary>
    [Required, MaxLength(10)]
    public string AgeKey { get; set; } = string.Empty;

    // ── Ось 3: дисциплина ────────────────────────────────────────────────────

    /// <summary>male | female | mixed (смешанная эстафета) — см. <see cref="Genders"/>.</summary>
    [Required, MaxLength(10)]
    public string Gender { get; set; } = string.Empty;

    /// <summary>25m | 50m (клиентские ключи 25m_pool/50m_pool нормализуются при сидинге).</summary>
    [Required, MaxLength(5)]
    public string PoolType { get; set; } = string.Empty;

    /// <summary>Ключ стиля как на клиенте: freestyle/backstroke/breaststroke/butterfly/medley/….
    /// Намеренно НЕ FK на Styles — клиент матчится по этим строковым ключам.</summary>
    [Required, MaxLength(30)]
    public string Style { get; set; } = string.Empty;

    /// <summary>50m, 100m, …, 4X50m (эстафеты).</summary>
    [Required, MaxLength(10)]
    public string Distance { get; set; } = string.Empty;

    // ── Собственно рекорд ────────────────────────────────────────────────────

    /// <summary>Время в исходном строковом формате ("21.08", "01:43.45") — клиент
    /// сравнивает/парсит строки, как делал с JS-данными.</summary>
    [Required, MaxLength(15)]
    public string Time { get; set; } = string.Empty;

    /// <summary>
    /// То же время в миллисекундах — чтобы сортировать и сравнивать в БД, а не строкой:
    /// «01:02.36» &lt; «21.08» лексикографически, но не по секундомеру. Нужно рейтингу
    /// рекордов (план Фазы 11, этап 11.2).
    ///
    /// ⚠ Колонка ВЫЧИСЛЯЕМАЯ (Postgres GENERATED ... STORED из <see cref="Time"/>), писать
    /// её нельзя — попытка присвоить кончится ошибкой БД. Так сделано потому, что рекорды
    /// пишут четыре разных места (дифф импорта, две формы админки, сидер), а ещё psql и
    /// восстановление дампа: любая ручная синхронизация двух колонок рано или поздно
    /// разъезжается, а вычисляемая не может по построению.
    ///
    /// null — строку времени разобрать не удалось. Это не ошибка сама по себе: в справочнике
    /// встречаются пометки вместо времени, и терять из-за них строку целиком хуже.
    /// </summary>
    public int? TimeMs { get; private set; }

    /// <summary>Держатель(и): у эстафет — несколько имён через запятую; у age/masters — иврит.</summary>
    [MaxLength(400)]
    public string? HolderName { get; set; }

    /// <summary>
    /// Держатель ЛАТИНИЦЕЙ — для международных экранов (`/records`, `/records/compare`),
    /// где ивритская строка посреди рейтинга двух сотен стран читается как сбой кодировки
    /// (решение Влада 16.09.2026, исключение в docs/important.md).
    ///
    /// Заполняет источник, который сам отдаёт латиницу (World Aquatics); федерация пишет
    /// иврит в <see cref="HolderName"/> и это поле не трогает.
    ///
    /// ⚠ **Имя привязано к КОНКРЕТНОМУ рекорду, а не к дисциплине.** Поэтому при смене
    /// <see cref="Time"/> оно сбрасывается: иначе следующий рекорд той же дисциплины оказался
    /// бы подписан именем предыдущего держателя. Правило целиком — в
    /// <c>RecordDiffService.ResolveHolderNameEn</c>.
    /// </summary>
    [MaxLength(400)]
    public string? HolderNameEn { get; set; }

    /// <summary>
    /// Рекорд проплыт ПЕРВЫМ ЭТАПОМ эстафеты (время первого этапа засчитывается личным).
    /// Ручная пометка админа (/Admin/Records) — для случаев, когда протокола у нас нет
    /// (рекорды 2014 года и старше). Где эстафета с промежуточными есть в базе, витрина
    /// узнаёт это сама (<c>SwimmerPageRepository.GetRecordsHeldAsync</c>), галка не нужна.
    /// Привязана к конкретному значению: смена <see cref="Time"/> её снимает.
    /// </summary>
    public bool IsRelayLeadOff { get; set; }

    /// <summary>Клуб держателя (age/masters-рекорды).</summary>
    [MaxLength(200)]
    public string? Club { get; set; }

    /// <summary>Страна ДЕРЖАТЕЛЯ — не путать с территорией рекорда: мировой рекорд (world)
    /// держит спортсмен конкретной страны (CAY, AUS, …).</summary>
    [MaxLength(10)]
    public string? HolderCountry { get; set; }

    /// <summary>Дата рекорда в исходном строковом виде (обычно dd/MM/yyyy; в легаси-данных
    /// встречаются и M/d/yyyy — нормализация руками через админ-CRUD, не сидером).</summary>
    [MaxLength(20)]
    public string? RecordDate { get; set; }

    /// <summary>Когда МЫ обновили эту запись (UTC) — не путать с <see cref="RecordDate"/>
    /// (дата установления рекорда спортсменом). Проставляется при создании/правке/апдейте
    /// диффа; сидер ставит время сидинга.</summary>
    public DateTime UpdatedAt { get; set; }
}
