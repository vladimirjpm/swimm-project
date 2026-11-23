namespace Swimm.Application.Mapping;

/// <summary>
/// Статус шага чек-листа правой панели /Admin/Competitions (вариант E, хэндофф 3.5): кружок у шага.
/// </summary>
public enum ChecklistStepStatus
{
    /// <summary>Серый с номером — ещё не дошли.</summary>
    Pending,
    /// <summary>Голубой с номером — текущий шаг.</summary>
    Current,
    /// <summary>Зелёный «✓» — сделано.</summary>
    Done,
    /// <summary>Жёлтый «!» — пропущено или сорвалось, нужно внимание.</summary>
    Attention,
}

/// <summary>
/// Ход одиночного затягивания, который знает только страница (разбор, превью, импорт идут
/// запросами из браузера и живут в его sessionStorage). Сервер рисует чек-лист с
/// <see cref="None"/> и отдаёт странице статусы шагов на каждую фазу — пересчитывать их в JS не нужно.
/// </summary>
public enum ChecklistPhase
{
    /// <summary>Затягивания нет — статусы по данным строки.</summary>
    None,
    /// <summary>Скачиваю PDF и разбираю.</summary>
    Parsing,
    /// <summary>Разбор не удался.</summary>
    ParseFailed,
    /// <summary>Превью готово, ждёт «Импортировать».</summary>
    Preview,
    /// <summary>Превью устарело (живёт 60 минут) — затянуть заново.</summary>
    PreviewExpired,
    /// <summary>Импорт в фоне.</summary>
    Importing,
    /// <summary>Импорт не прошёл.</summary>
    ImportFailed,
    /// <summary>
    /// Тянуть нечего (Р116): разбор ответил «пустой PDF» или «все строки — неявка». Это не сбой —
    /// сервер уже поставил «∅ нет протокола», строка закрыта; шаг 5 решён, дальше идти некуда.
    /// </summary>
    NothingToPull,
}

/// <summary>
/// Что нужно правилу о строке. Всё — из того, что у строки списка уже есть
/// (<see cref="Dtos.UnifiedCompetitionRowDto"/> + её <see cref="CompetitionRowStatus"/>).
/// </summary>
/// <param name="State">Рабочее состояние строки.</param>
/// <param name="Imported">Строка уже в базе (импортирована).</param>
/// <param name="StartList">Исход последнего забора стартового протокола; null — не забирали.</param>
/// <param name="StartListStale">Стартовый протокол пора обновить (до старта мало дней, забору больше суток).</param>
/// <param name="WarmUpDays">Сколько дней старта с заданной разминкой.</param>
/// <param name="DayCount">Дней старта (по датам сайта).</param>
/// <param name="QualityScanned">Проверка качества уже прогонялась.</param>
/// <param name="SuspectCount">Строк, помеченных сомнительными.</param>
/// <param name="OnFederationSite">Строка есть у федерации (не PDF-импорт без OrgCompId) — шаг 1.</param>
/// <param name="Incomplete">
/// У затянутого протокол неполный: видимая отметка «результаты неполные» (Р114) или затянуты не все
/// дни по сайту федерации (Р115) — шаг 5 с «!». У дня события — по отметке этого дня.
/// </param>
/// <param name="Hidden">Строка скрыта (Ignored) — шаг 5 серый: «скрыто», а не «тянуть нечего».</param>
public sealed record ChecklistInput(
    CompetitionRowState State,
    bool Imported = false,
    StartListBadgeKind? StartList = null,
    bool StartListStale = false,
    int WarmUpDays = 0,
    int DayCount = 1,
    bool QualityScanned = false,
    int SuspectCount = 0,
    bool OnFederationSite = true,
    bool Incomplete = false,
    bool Hidden = false);

/// <summary>
/// Девять шагов чек-листа (хэндофф 3.5): 1 найден у федерации, 2 стартовый протокол, 3 разминка,
/// 4 старт прошёл, 5 протокол скачан и разобран, 6 проверь превью, 7 импорт, 8 проверка качества,
/// 9 после импорта. Шаги 1–4 — по данным строки; 5–9 — по состоянию и фазе затягивания.
///
/// С этапа 6 чек-лист — у строк всех состояний (таблица случаев — хэндофф 3.5 и
/// docs/admin-pages/competitions.md); у многодневного события шаги 5–9 считаются по каждому дню.
/// </summary>
public static class CompetitionChecklistRules
{
    public const int StepCount = 9;

    /// <summary>Фазы, которые страница может включить без перезагрузки (у каждой — свой набор статусов).</summary>
    public static readonly IReadOnlyList<ChecklistPhase> LivePhases =
    [
        ChecklistPhase.Parsing, ChecklistPhase.ParseFailed, ChecklistPhase.Preview,
        ChecklistPhase.PreviewExpired, ChecklistPhase.Importing, ChecklistPhase.ImportFailed,
        ChecklistPhase.NothingToPull,
    ];

    /// <summary>Статусы девяти шагов (индекс 0 — шаг 1).</summary>
    public static IReadOnlyList<ChecklistStepStatus> Build(ChecklistInput input, ChecklistPhase phase = ChecklistPhase.None)
    {
        var s = new ChecklistStepStatus[StepCount];
        var notStarted = input.State is CompetitionRowState.Ahead or CompetitionRowState.Running;

        // PDF-импорт без страницы федерации (DbOnly) — «найден у федерации» не про него.
        s[0] = input.OnFederationSite ? ChecklistStepStatus.Done : ChecklistStepStatus.Pending;
        // Шаги 2–3 у прошедшего старта («ждёт затягивания», «ждём протокол», закрыто): несделанное —
        // серым, без «!» (решение Влада 23.11.2026): после старта их уже никто не задаст, а у долга
        // прошлых сезонов «!» был бы шумом. До старта и пока он идёт — «!» остаётся.
        var missed = notStarted ? ChecklistStepStatus.Attention : ChecklistStepStatus.Pending;
        s[1] = StartListStatus(input, notStarted) == ChecklistStepStatus.Done ? ChecklistStepStatus.Done : missed;
        s[2] = input.WarmUpDays >= Math.Max(1, input.DayCount) ? ChecklistStepStatus.Done : missed;
        s[3] = notStarted ? ChecklistStepStatus.Current : ChecklistStepStatus.Done;

        var tail = phase == ChecklistPhase.None ? ByState(input) : ByPhase(phase);
        Array.Copy(tail, 0, s, 4, 5);
        return OneCurrent(s);
    }

    /// <summary>
    /// Инвариант: текущий шаг — не больше одного. Конфликт бывает, когда старт ещё «впереди» / «идёт»
    /// (шаг 4 текущий), а его уже тянут вручную (хэндофф 2: «пока старт идёт, тянуть можно») —
    /// текущим остаётся шаг хода, а шаг 4 получает «!»: протокол идущего старта может быть неполным.
    /// </summary>
    private static ChecklistStepStatus[] OneCurrent(ChecklistStepStatus[] s)
    {
        var last = Array.LastIndexOf(s, ChecklistStepStatus.Current);
        for (var i = 0; i < last; i++)
            if (s[i] == ChecklistStepStatus.Current) s[i] = ChecklistStepStatus.Attention;
        return s;
    }

    /// <summary>Подпись статуса для разметки (data-status) и JS страницы.</summary>
    public static string Code(ChecklistStepStatus status) => status switch
    {
        ChecklistStepStatus.Current => "current",
        ChecklistStepStatus.Done => "done",
        ChecklistStepStatus.Attention => "attention",
        _ => "pending",
    };

    /// <summary>Ключ фазы в JS страницы (sessionStorage хранит его же).</summary>
    public static string Code(ChecklistPhase phase) => phase switch
    {
        ChecklistPhase.Parsing => "parsing",
        ChecklistPhase.ParseFailed => "parse-failed",
        ChecklistPhase.Preview => "preview",
        ChecklistPhase.PreviewExpired => "preview-expired",
        ChecklistPhase.Importing => "importing",
        ChecklistPhase.ImportFailed => "import-failed",
        ChecklistPhase.NothingToPull => "nothing",
        _ => "none",
    };

    // Стартовый протокол: забран полностью или частично — сделано; пусто, ошибка, не забирали — «!».
    // До старта устаревший — тоже «!» (та же пометка «обновить», что в строке списка).
    private static ChecklistStepStatus StartListStatus(ChecklistInput i, bool notStarted) => i.StartList switch
    {
        StartListBadgeKind.Ok or StartListBadgeKind.Partial when !(notStarted && i.StartListStale) => ChecklistStepStatus.Done,
        _ => ChecklistStepStatus.Attention,
    };

    private static ChecklistStepStatus[] ByState(ChecklistInput i)
    {
        const ChecklistStepStatus P = ChecklistStepStatus.Pending, C = ChecklistStepStatus.Current,
            D = ChecklistStepStatus.Done, A = ChecklistStepStatus.Attention;

        if (i.Imported)
        {
            // Шаг 9 — «действия доступны» (хэндофф 3.5), текущим он становится, только когда
            // проверка качества уже прогнана; до того текущий — шаг 8.
            var quality = i.SuspectCount > 0 ? A : i.QualityScanned ? D : C;
            // Неполный протокол у затянутого (Р114 / Р115) — «!» у шага 5: ждём остальное или «Принять как есть».
            return [i.Incomplete ? A : D, D, D, quality, quality == C ? P : C];
        }
        return i.State switch
        {
            CompetitionRowState.ReadyToPull => [C, P, P, P, P],
            // Ждём протокол — шаг 5 «!» с причиной подписью.
            CompetitionRowState.WaitingProtocol => [A, P, P, P, P],
            // Закрыто без импорта: скрыто — серое; «∅ нет протокола» и «✓ имп.» руками — шаг 5 решён
            // («протокола нет» / «помечено импортированным»), дальше идти некуда.
            CompetitionRowState.Closed => [i.Hidden ? P : D, P, P, P, P],
            _ => [P, P, P, P, P],
        };
    }

    private static ChecklistStepStatus[] ByPhase(ChecklistPhase phase)
    {
        const ChecklistStepStatus P = ChecklistStepStatus.Pending, C = ChecklistStepStatus.Current,
            D = ChecklistStepStatus.Done, A = ChecklistStepStatus.Attention;
        return phase switch
        {
            ChecklistPhase.Parsing => [C, P, P, P, P],
            ChecklistPhase.ParseFailed => [A, P, P, P, P],
            ChecklistPhase.Preview => [D, C, P, P, P],
            ChecklistPhase.PreviewExpired => [D, A, P, P, P],
            ChecklistPhase.Importing => [D, D, C, P, P],
            ChecklistPhase.ImportFailed => [D, D, A, P, P],
            ChecklistPhase.NothingToPull => [D, P, P, P, P],
            _ => [P, P, P, P, P],
        };
    }
}
