using Microsoft.AspNetCore.WebUtilities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.API.Pages.Admin.Shared;

namespace Swimm.API.Pages.Admin.Competitions;

/// <summary>
/// Соревнования: объединённый список справочника БД (Competitions) и входящих isr.org.il
/// (Discovery) одной таблицей со стадией жизненного цикла. Заменил обе прежние страницы
/// (Competitions/Index + Discovery). Действия входящих — через /api/admin/discovery/*
/// (клиентский JS); CRUD БД — здесь (Edit-страница + каскадное удаление).
/// </summary>
[Authorize(Roles = "Admin")]
public class IndexModel : PageModel
{
    private const int PageSize = CompetitionListUrl.PageSize;
    private readonly TimeProvider _time;
    private readonly ICompetitionAdminRepository _repo;
    private readonly IImportService _import;
    private readonly IPointRulesAdminRepository _rules;
    private readonly IAdminAuditService _audit;
    private readonly IConfiguration _config;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<IndexModel> _logger;
    private readonly ISeriesAdminRepository _series;

    public IndexModel(ICompetitionAdminRepository repo, IImportService import,
        IPointRulesAdminRepository rules, IAdminAuditService audit,
        IConfiguration config, IWebHostEnvironment env, ILogger<IndexModel> logger,
        ISeriesAdminRepository series, TimeProvider time)
    {
        _time = time;
        _series = series;
        _repo = repo;
        _import = import;
        _rules = rules;
        _audit = audit;
        _config = config;
        _env = env;
        _logger = logger;
    }

    [BindProperty(SupportsGet = true, Name = "q")]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true, Name = "cat")]
    public string? CategoryKey { get; set; }

    /// <summary>
    /// Сырой <c>?season=</c>: год окончания сезона (2025 = 24/25, совпадает с cYear на isr.org.il)
    /// или <see cref="CompetitionListUrl.AllSeasons"/> — явный выбор чипа «Все». Строкой, а не
    /// числом: «all» нужен, чтобы голый адрес (= текущий месяц) отличался от «все сезоны».
    /// </summary>
    [BindProperty(SupportsGet = true, Name = "season")]
    public string? SeasonParam { get; set; }

    /// <summary>Выбранный сезон (год окончания); null — все сезоны.</summary>
    public int? Season => CompetitionListUrl.ParseSeason(SeasonParam);

    /// <summary>
    /// Фильтр по корзине состояния: <c>?state=waiting|ahead|closed</c>
    /// (<see cref="CompetitionStateFilters"/>; «Ждут / Впереди / Закрыто»). Пусто — все.
    /// Комбинируется со <c>stage</c>, на счётчики сезонов/месяцев/переключателя не влияет.
    /// </summary>
    [BindProperty(SupportsGet = true, Name = "state")]
    public string? State { get; set; }

    /// <summary>Разобранный <see cref="State"/>; null — фильтра нет (пусто или незнакомое значение).</summary>
    public CompetitionStateBucket? StateBucket => CompetitionStateFilters.Parse(State);

    /// <summary>
    /// Выбранное соревнование — ключ строки (<see cref="CompetitionRowKey"/>: <c>e{EventId}</c>,
    /// <c>c{Id}</c>, <c>s{DiscoveredId}</c>). Этапы 3–4 откроют по нему правую панель вместо
    /// раскрытых строк в sessionStorage; на этапе 1 только разбирается и сохраняется в ссылках.
    /// </summary>
    [BindProperty(SupportsGet = true, Name = "sel")]
    public string? Selected { get; set; }

    /// <summary>Строка под <see cref="Selected"/> среди строк страницы; null — не выбрана или не видна при фильтрах.</summary>
    public UnifiedCompetitionRowDto? SelectedRow { get; private set; }

    /// <summary>
    /// Строка, открытая справа без <c>sel</c>: первая «ждёт затягивания» выдачи (решение Влада
    /// 23.11.2026). Только показ — в адрес и ссылки не попадает; при явном <c>sel</c> — null.
    /// </summary>
    public UnifiedCompetitionRowDto? AutoSelectedRow { get; private set; }

    /// <summary>Сегодня по Израилю — граница «впереди / идёт / прошёл».</summary>
    public DateOnly Today { get; private set; }

    /// <summary>
    /// Без пагинации: выбраны И сезон, И месяц — месяц показывается целиком (10–25 строк). Месяц
    /// без сезона (<c>season=all&amp;month=</c>) — это тот же месяц за все годы, там обычная «p» по 20.
    /// </summary>
    public bool Unpaged => Season != null && Month != null;

    /// <summary>Адрес был пустым — подставлены текущий сезон и месяц (хэндофф, раздел 6.1).</summary>
    public bool OpenedOnCurrentMonth { get; private set; }

    /// <summary>
    /// Состояния по месяцам (индекс 0 = январь) — с фильтром сезона, без месяца, как
    /// <see cref="MonthCounts"/>. Порядок полосы сезона — <see cref="CompetitionRowStateRules.SeasonMonthOrder"/>.
    /// </summary>
    public IReadOnlyList<CompetitionStateCounts> MonthStates { get; private set; } =
        Enumerable.Repeat(CompetitionStateCounts.Zero, 12).ToList();

    /// <summary>Состояния всего списка под текущими фильтрами (с месяцем), без <c>?state=</c> — числа переключателя.</summary>
    public CompetitionStateCounts ListStates { get; private set; } = CompetitionStateCounts.Zero;

    /// <summary>Строки страницы «ждёт затягивания», по блокам дней (последний день старта), свежий день первым.</summary>
    public IReadOnlyList<ReadyToPullDay<UnifiedCompetitionRowDto>> ReadyToPullByDay { get; private set; } = [];

    /// <summary>
    /// Кнопка «Затянуть за месяц (N)» — когда выбраны сезон И месяц (<see cref="Unpaged"/>: месяц виден
    /// целиком). Прочие фильтры (дисциплина, filter=, stage, state…) при этом сужают выборку, и кнопка
    /// берёт отфильтрованное подмножество месяца — число на ней честное. null — месяца сезона нет
    /// (поиск переводит на все сезоны, весь сезон, season=all): кнопки нет (хэндофф 4).
    /// Отбор — <see cref="BulkPullSelection"/>, тот же, что у кнопок дней.
    /// </summary>
    public BulkPullOffer? MonthBulk { get; private set; }

    /// <summary>
    /// «Затянуть за день (N)» по блокам дней «Ждут затягивания». Пусто — кнопок дней нет: список
    /// разбит на страницы, и блок дня на этой странице мог быть неполным.
    /// </summary>
    public IReadOnlyDictionary<DateOnly, BulkPullOffer> DayBulk { get; private set; } =
        new Dictionary<DateOnly, BulkPullOffer>();

    /// <summary>Тип соревнования: «champ» — только чемпионаты (по названию), пусто — все.</summary>
    [BindProperty(SupportsGet = true, Name = "kind")]
    public string? Kind { get; set; }

    /// <summary>
    /// Вид спорта: пусто — только плавание (дефолт), «all» — всё, иначе конкретная дисциплина.
    /// Артистическое плавание из списка прячем, но не удаляем — см. Disciplines.
    /// </summary>
    [BindProperty(SupportsGet = true, Name = "discipline")]
    public string? Discipline { get; set; }

    /// <summary>Сколько строк спрятал фильтр дисциплины — чип «скрыто N» над списком.</summary>
    public int HiddenByDiscipline { get; private set; }

    /// <summary>Фильтр по стадии: OnSite | Imported | DbOnly | Ignored (пусто — все).</summary>
    [BindProperty(SupportsGet = true, Name = "stage")]
    public string? Stage { get; set; }

    /// <summary>
    /// Deep-link алиас с дашборда: ignored → Stage=Ignored, discovery-new → Stage=OnSite
    /// (только если Stage не задан явно явным параметром). Остальные значения (discovery-error,
    /// no-org-comp-id, no-results) — T3b, идут в <see cref="QualityFilter"/> доп-WHERE'ом.
    /// </summary>
    [BindProperty(SupportsGet = true, Name = "filter")]
    public string? FilterAlias { get; set; }

    /// <summary>Разобранный из FilterAlias качественный фильтр (T3b) — для шапки списка/ссылки «сбросить».</summary>
    public string? QualityFilter { get; private set; }

    /// <summary>Показывать тестовую синтетику (SYNTH Meet…). По умолчанию скрыта.</summary>
    [BindProperty(SupportsGet = true, Name = "synth")]
    public bool ShowSynthetic { get; set; }

    /// <summary>Фильтр по месяцу 1–12 (null — все). Кнопки-месяцы над списком.</summary>
    [BindProperty(SupportsGet = true, Name = "month")]
    public int? Month { get; set; }

    // ВНИМАНИЕ: имя параметра — «p», а НЕ «page»: «page» зарезервировано роутингом Razor Pages
    // (и для Url.Page-генерации, и для байндинга), из-за чего пагинация молча ломается.
    [BindProperty(SupportsGet = true, Name = "p")]
    public int PageNumber { get; set; } = 1;

    public PagedResult<UnifiedCompetitionRowDto> Result { get; private set; } =
        new([], 0, 1, PageSize);

    /// <summary>Счётчики соревнований по месяцам (индекс 0 = январь) для кнопок-фильтров.</summary>
    public IReadOnlyList<int> MonthCounts { get; private set; } = new int[12];

    /// <summary>Из <see cref="MonthCounts"/> — сколько уже затянуто в БД (для «2/5» на кнопке месяца).</summary>
    public IReadOnlyList<int> MonthImported { get; private set; } = new int[12];

    /// <summary>Из <see cref="MonthCounts"/> — сколько затянуть НЕЛЬЗЯ (нечего брать).</summary>
    public IReadOnlyList<int> MonthNothingToPull { get; private set; } = new int[12];

    public IReadOnlyList<CategoryTagDto> Categories { get; private set; } = [];

    /// <summary>Чипы-сезоны над списком: сезон + «затянуто из всего». Считаются по тем же
    /// фильтрам, что и список, но без фильтров сезона/месяца — иначе чип остался бы один.</summary>
    public IReadOnlyList<SeasonCountDto> SeasonCounts { get; private set; } = [];

    /// <summary>Правила очков и типы бассейна — для селектов панели быстрой правки строки.</summary>
    /// <summary>База для ссылок «смотреть на сайте» — <see cref="PublicSite.BaseUrl"/>.</summary>
    public string PublicSiteBaseUrl { get; private set; } = "";

    public IReadOnlyList<PointRuleRowDto> ClubRules { get; private set; } = [];
    public IReadOnlyList<PointRuleRowDto> SwimmerRules { get; private set; } = [];
    public IReadOnlyList<string> PoolTypeOptions => Swimm.Application.Constants.PoolTypes.All;

    /// <summary>Серии с ключевыми фразами — чип «серия» у строки входящих (§5.8 плана official-awards).</summary>
    public IReadOnlyList<SeriesRowDto> SeriesWithPhrases { get; private set; } = [];

    /// <summary>Серия, под фразу которой подходит название с сайта; null — ни одной или несколько.</summary>
    public SeriesRowDto? SeriesFor(string? name)
    {
        var matched = SeriesWithPhrases
            .Where(x => Swimm.Application.Mapping.SeriesMatch.NameMatches(name,
                (x.MatchPhrases ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)))
            .Take(2).ToList();
        return matched.Count == 1 ? matched[0] : null;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        // Отправка формы фильтров (fseason/fmonth) → чистый адрес: поиск по всем сезонам, прочие
        // фильтры — в выбранном месяце (решение Влада 23.11.2026, CompetitionListUrl.FormRedirect).
        if (CompetitionListUrl.FormRedirect(
                Request.Query.Select(kv => new KeyValuePair<string, string?>(kv.Key, kv.Value.ToString()))) is { } canonical)
            return Redirect(QueryHelpers.AddQueryString(Url.Page("Index")!, canonical!));

        PublicSiteBaseUrl = PublicSite.BaseUrl(_config, _env);
        Today = CompetitionRowStateRules.IsraelToday(_time.GetUtcNow());

        // Голый адрес — текущий сезон и месяц. Любой непустой параметр — выборка задана явно.
        if (CompetitionListUrl.ShouldOpenOnCurrentMonth(
                Request.Query.Select(kv => new KeyValuePair<string, string?>(kv.Key, kv.Value.ToString()))))
        {
            var (season, month) = CompetitionListUrl.CurrentSeasonMonth(Today);
            SeasonParam = season.ToString(System.Globalization.CultureInfo.InvariantCulture);
            Month = month;
            OpenedOnCurrentMonth = true;
        }
        if (StateBucket == null) State = null;
        if (!CompetitionRowKey.IsValid(Selected)) Selected = null;
        if (Month is not (>= 1 and <= 12)) Month = null;

        if (PageNumber < 1) PageNumber = 1;
        if (string.IsNullOrEmpty(Stage))
        {
            Stage = FilterAlias switch
            {
                "ignored" => "Ignored",
                "discovery-new" => "OnSite",
                _ => Stage
            };
        }
        QualityFilter = FilterAlias switch
        {
            "discovery-error" or "no-org-comp-id" or "no-results" => FilterAlias,
            // results-auto (П5): ссылка с дашборда «ждут тебя / взяла бы сама».
            Swimm.Application.Mapping.AutoPullFilters.Blocked or Swimm.Application.Mapping.AutoPullFilters.WouldTake => FilterAlias,
            _ => null
        };
        // Выбраны сезон И месяц — месяц целиком, без пагинации; иначе (в т.ч. месяц за все годы
        // при season=all) — «p» по 20.
        var list = await _repo.GetUnifiedAsync(Search, CategoryKey, Season, Stage, ShowSynthetic, Month,
            PageNumber, PageSize, QualityFilter, Kind, Discipline,
            state: State, today: Today, unpaged: Unpaged);
        Result = list.Page;
        MonthCounts = list.MonthCounts;
        MonthImported = list.MonthImported ?? new int[12];
        MonthNothingToPull = list.MonthNothingToPull ?? new int[12];
        SeasonCounts = list.SeasonCounts ?? [];
        MonthStates = list.MonthStates ?? MonthStates;
        ListStates = list.ListStates ?? CompetitionStateCounts.Zero;
        ReadyToPullByDay = CompetitionRowStateRules.GroupReadyToPullByDay(Result.Items, u => u.Status);
        // Пачки (хэндофф 4): N на кнопке и id в ней — одним правилом BulkPullSelection.
        MonthBulk = Unpaged ? BulkPullSelection.Offer(Result.Items) : null;
        DayBulk = Unpaged || Result.TotalPages <= 1
            ? ReadyToPullByDay.ToDictionary(d => d.Day, d => BulkPullSelection.Offer(d.Rows))
            : new Dictionary<DateOnly, BulkPullOffer>();
        SelectedRow = Selected == null ? null : Result.Items.FirstOrDefault(u => CompetitionRowKey.Matches(u, Selected));
        // Без sel справа сразу первая «ждёт затягивания» (самый свежий день, первая строка блока) —
        // решение Влада 23.11.2026. В адрес не пишется: голый адрес остаётся голым. Явный sel главнее.
        AutoSelectedRow = Selected == null ? ReadyToPullByDay.FirstOrDefault()?.Rows.FirstOrDefault() : null;
        HiddenByDiscipline = list.HiddenByDiscipline;
        Categories = await _repo.GetAllCategoriesAsync();
        ClubRules = await _rules.GetAllAsync(PointRuleKind.Clubs);
        SwimmerRules = await _rules.GetAllAsync(PointRuleKind.Swimmers);
        SeriesWithPhrases = (await _series.GetAllAsync()).Where(x => !string.IsNullOrEmpty(x.MatchPhrases)).ToList();
        return Page();
    }

    /// <summary>
    /// Быстрая правка из раскрытой панели строки: бассейн, флаги, категории, правила очков —
    /// сразу всем дням события (регламент у события один). Имя/дата/страна правятся на Edit.
    /// </summary>
    public async Task<IActionResult> OnPostQuickSaveAsync(int id, string poolType,
        bool isAward, bool isChampionship, bool showCombineAllResults,
        List<string>? categoryKeys, int? pointRuleClubsId, int? pointRuleSwimmersId,
        bool clubPointsDisabled = false)
    {
        var result = await _repo.QuickUpdateAsync(new CompetitionQuickEditDto
        {
            CompetitionId = id,
            PoolType = poolType ?? "",
            IsAward = isAward,
            IsChampionship = isChampionship,
            ShowCombineAllResults = showCombineAllResults,
            CategoryKeys = categoryKeys ?? [],
            PointRuleClubsId = pointRuleClubsId,
            PointRuleSwimmersId = pointRuleSwimmersId,
            ClubPointsDisabled = clubPointsDisabled
        });

        if (!result.Success)
        {
            TempData["Flash"] = $"Не сохранено: {result.Error}";
            return RedirectToBackToList();
        }

        // Id в результате — число изменённых дней.
        await _audit.LogAsync("competition.quick-edit", "Competition", id.ToString(),
            $"Быстрая правка из списка (#{id}): бассейн={poolType}, awards={isAward}, " +
            $"чемпионат={isChampionship}, combine={showCombineAllResults}, " +
            $"категории=[{string.Join(",", categoryKeys ?? [])}], " +
            $"клубный зачёт={(clubPointsDisabled ? "не ведётся" : "ведётся")}, дней изменено: {result.Id}");

        TempData["Flash"] = result.Id > 1
            ? $"Сохранено, применено ко всем дням события: {result.Id}"
            : "Сохранено";
        return RedirectToBackToList();
    }

    /// <summary>Каскадное удаление одного соревнования (одиночного или отдельного дня события).</summary>
    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var deleted = await _import.DeleteCompetitionAsync(id);
        if (deleted == null)
        {
            TempData["Flash"] = "Соревнование не найдено — возможно, уже удалено.";
            return RedirectToBackToList(withSelected: KeepsSelectionAfterDelete);
        }

        _logger.LogWarning(
            "Admin {User} каскадно удалил соревнование #{Id} «{Name}»: {Results} результатов, " +
            "{Relays} эстафет, {Galleries} галерей, {ResultUrls} URL, {ImportHistory} записей истории, {Swimmers} пловцов-сирот",
            User.Identity?.Name ?? "?", deleted.CompetitionId, deleted.CompetitionName,
            deleted.Results, deleted.Relays, deleted.Galleries, deleted.ResultUrls, deleted.ImportHistory, deleted.Swimmers);

        await _audit.LogAsync("competition.delete", "Competition", deleted.CompetitionId.ToString(),
            $"Каскадно удалено соревнование «{deleted.CompetitionName}» ({deleted.Results} результатов)",
            deleted);

        TempData["Flash"] = $"Соревнование «{deleted.CompetitionName}» удалено ({deleted.Results} результатов)";
        return RedirectToBackToList(withSelected: KeepsSelectionAfterDelete);
    }

    /// <summary>Каскадное удаление всего многодневного события: все дни + сам CompetitionEvent.</summary>
    public async Task<IActionResult> OnPostDeleteEventAsync(int eventId)
    {
        var deleted = await _import.DeleteCompetitionEventAsync(eventId);
        if (deleted == null)
        {
            TempData["Flash"] = "Событие не найдено — возможно, уже удалено.";
            return RedirectToBackToList(withSelected: false);
        }

        _logger.LogWarning(
            "Admin {User} каскадно удалил многодневное событие #{Id} «{Name}»: {Results} результатов, " +
            "{Relays} эстафет, {Galleries} галерей, {ResultUrls} URL, {ImportHistory} записей истории, {Swimmers} пловцов-сирот",
            User.Identity?.Name ?? "?", deleted.CompetitionId, deleted.CompetitionName,
            deleted.Results, deleted.Relays, deleted.Galleries, deleted.ResultUrls, deleted.ImportHistory, deleted.Swimmers);

        await _audit.LogAsync("competition.delete-event", "CompetitionEvent", eventId.ToString(),
            $"Каскадно удалено многодневное событие «{deleted.CompetitionName}» ({deleted.Results} результатов)",
            deleted);

        TempData["Flash"] = $"Событие «{deleted.CompetitionName}» удалено вместе со всеми днями ({deleted.Results} результатов)";
        return RedirectToBackToList(withSelected: false);
    }

    /// <summary>
    /// Параметры текущей выборки — ОДИН список на ссылки, формы и редиректы после POST
    /// (раньше их было шесть копий, и в редиректе быстрой правки потерялись discipline и filter).
    /// Пустые не попадают: «назад» в браузере возвращает ту же выборку. <c>p</c> — только при
    /// <paramref name="withPage"/>, <c>sel</c> — при <paramref name="withSelected"/>.
    /// </summary>
    public Dictionary<string, string> ListRoute(bool withPage = true, bool withSelected = true)
    {
        var d = new Dictionary<string, string>();
        void Put(string key, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value)) d[key] = value;
        }

        Put("q", Search);
        Put("cat", CategoryKey);
        Put("season", SeasonParam);
        Put("kind", Kind);
        Put("discipline", Discipline);
        Put("stage", Stage);
        Put("state", State);
        Put("synth", ShowSynthetic ? "true" : null);
        Put("month", Month?.ToString(System.Globalization.CultureInfo.InvariantCulture));
        // На GET алиасы ignored / discovery-new уже превращены в stage, остальное — в QualityFilter;
        // на POST QualityFilter не разбирается, там идёт сырой filter из адреса формы.
        Put("filter", QualityFilter ?? (Request.Method == HttpMethods.Get ? null : FilterAlias));
        // С месяцем пагинации нет — «p» в адресе был бы мусором.
        if (withPage && !Unpaged) Put("p", (Result.Items.Count > 0 ? Result.Page : PageNumber).ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (withSelected) Put("sel", Selected);
        return d;
    }

    // Сохраняем текущие фильтры/страницу при возврате к списку. После удаления выбранной строки
    // sel не несём (withSelected: false): её больше нет, и вместо «Выбери соревнование…» стояла бы
    // заглушка «не видно при текущих фильтрах».
    private IActionResult RedirectToBackToList(bool withSelected = true) =>
        RedirectToPage("Index", new Microsoft.AspNetCore.Routing.RouteValueDictionary(
            ListRoute(withSelected: withSelected).ToDictionary(kv => kv.Key, kv => (object?)kv.Value)));

    // «Удалить» у дня события: само событие остаётся (если дней больше одного) — его выбор (sel=e…)
    // сохраняем. Удалили одиночное (sel=c…) — выбора больше нет.
    private bool KeepsSelectionAfterDelete =>
        Selected?.Trim() is { Length: > 1 } k && (k[0] is 'e' or 'E');
}
