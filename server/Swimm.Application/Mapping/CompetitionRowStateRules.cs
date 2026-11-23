using Swimm.Application.Constants;
using Swimm.Application.Dtos;

namespace Swimm.Application.Mapping;

/// <summary>
/// Рабочее состояние строки /Admin/Competitions (хэндофф «вариант E», раздел 2; правило
/// согласовано с Владом 23.11.2026). Вычисляется ПОВЕРХ стадии (<see cref="CompetitionStage"/>),
/// сама стадия в данных не меняется.
/// </summary>
public enum CompetitionRowState
{
    /// <summary>Впереди: на сайте, первый день ещё не наступил. Стартовый протокол состояние не меняет.</summary>
    Ahead,
    /// <summary>Идёт: с первого дня по последний включительно — по датам, а не по вердикту «ещё идёт».</summary>
    Running,
    /// <summary>Ждёт затягивания: последний день прошёл, протокол есть (или автопроверка его не смотрела).</summary>
    ReadyToPull,
    /// <summary>Ждём протокол: последний день прошёл, протокола нет, он неполный или не разобрался.</summary>
    WaitingProtocol,
    /// <summary>Закрыто: в базе, скрыто или «∅ нет протокола».</summary>
    Closed,
}

/// <summary>Почему «ждём протокол» — подпись строки и шага 5 панели.</summary>
public enum WaitingProtocolReason
{
    /// <summary>«протокола нет»: PDF пуст (вердикт empty) или результаты на странице не опубликованы.</summary>
    NoProtocol,
    /// <summary>«неполный»: не все события проплыты (вердикт not-finished) или отметка «результаты неполные» у затянутого.</summary>
    Incomplete,
    /// <summary>«сбой разбора»: вердикт failed — забор или разбор упал (красная пометка).</summary>
    ParseFailed,
}

/// <summary>
/// Куда строка идёт в полосах сезонов и месяцев: три сегмента, четвёртого цвета нет
/// (решение Влада 23.11: «идёт» считается вместе с «впереди», «ждём протокол» — в долг).
/// </summary>
public enum CompetitionStateBucket
{
    Closed,
    Debt,
    Ahead,
}

/// <summary>
/// Значения <c>?state=</c> — фильтр по корзине (переключатель «Все / Ждут / Впереди / Закрыто»).
/// Пусто — все. <c>stage=</c> остаётся рабочим и комбинируется с ним.
/// </summary>
public static class CompetitionStateFilters
{
    /// <summary>«Ждут» = долг: ждёт затягивания + ждём протокол.</summary>
    public const string Waiting = "waiting";
    /// <summary>«Впереди» = впереди + идёт.</summary>
    public const string Ahead = "ahead";
    /// <summary>«Закрыто».</summary>
    public const string Closed = "closed";

    /// <summary>Значение параметра → корзина; пусто и незнакомое — null (фильтра нет).</summary>
    public static CompetitionStateBucket? Parse(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        Waiting => CompetitionStateBucket.Debt,
        Ahead => CompetitionStateBucket.Ahead,
        Closed => CompetitionStateBucket.Closed,
        _ => null,
    };
}

/// <summary>
/// Всё, что у строки списка известно для выбора состояния — без базы и сети.
/// </summary>
/// <param name="Stage">Стадия склейки базы и «Входящих».</param>
/// <param name="FirstDay">Первый день старта (местная дата); null — дата не читается.</param>
/// <param name="LastDay">Последний день; null — тот же, что первый.</param>
/// <param name="AutoPullVerdict">Вердикт results-auto (<see cref="ResultsAutoVerdicts"/>); null — задача строку не смотрела.</param>
/// <param name="NoProtocolMark">Отметка «∅ нет протокола» (<c>EmptySourceAt</c>).</param>
/// <param name="ResultsNotPublished">
/// Ошибка забора «результаты на странице не опубликованы»: детали страницы читались, а
/// loglig-iframe нет (<c>LastError</c> стоит при пустом <c>LogligId</c>). Пустой <c>LogligId</c>
/// без ошибки сюда НЕ входит — это «детали ещё не загружались», а не «нечего брать».
/// </param>
/// <param name="PartialResults">Видимая отметка «результаты неполные» (Р114) — бывает только у строк базы.</param>
/// <param name="MarkedImported">Строка «Входящих» помечена вручную «✓ имп.» (статус imported), а в
/// базе её нет — закрыл человек, тянуть не нужно.</param>
/// <param name="MissingSinceDay">Местный день, с которого строки нет в списке isr.org.il
/// (<c>MissingSince</c>, ставит суточный забор); null — на месте.</param>
public sealed record CompetitionRowStateInput(
    CompetitionStage Stage,
    DateOnly? FirstDay,
    DateOnly? LastDay,
    string? AutoPullVerdict = null,
    bool NoProtocolMark = false,
    bool ResultsNotPublished = false,
    bool PartialResults = false,
    bool MarkedImported = false,
    DateOnly? MissingSinceDay = null);

/// <summary>Итог: состояние, причина и пометки строки.</summary>
/// <param name="Reason">Только у <see cref="CompetitionRowState.WaitingProtocol"/>.</param>
/// <param name="RunningDay">«идёт · день N из M» — N (с 1); только у <see cref="CompetitionRowState.Running"/>.</param>
/// <param name="DayCount">M — дней по датам старта; у однодневного 1.</param>
/// <param name="NeedsYou">«ждёт тебя» — вердикт blocked (жёлтая пометка).</param>
/// <param name="ParseFailed">«сбой разбора» — вердикт failed (красная пометка).</param>
/// <param name="NothingToPull">Закрыто без импорта: скрыто, «∅» или «✓ имп.» вручную — серый цвет вместо зелёного.</param>
/// <param name="FirstDay">Первый день старта — для «впереди: через N дней».</param>
/// <param name="LastDay">Последний день — по нему строки «ждёт затягивания» группируются в блоки дней.</param>
/// <param name="MissingDays">Сколько дней строки нет на сайте федерации (от <c>MissingSince</c>); null — на месте.</param>
/// <param name="SuggestHide">«пропал N дн. — скрыть?»: старт прошёл, строка в долге и пропала
/// <see cref="CompetitionRowStateRules.MissingSuggestHideDays"/> дней назад или раньше. Сама
/// не закрывается — скрывает Влад кнопкой «Скрыть» (решение 23.11.2026).</param>
public sealed record CompetitionRowStatus(
    CompetitionRowState State,
    WaitingProtocolReason? Reason = null,
    int? RunningDay = null,
    int DayCount = 1,
    bool NeedsYou = false,
    bool ParseFailed = false,
    bool NothingToPull = false,
    DateOnly? FirstDay = null,
    DateOnly? LastDay = null,
    int? MissingDays = null,
    bool SuggestHide = false)
{
    /// <summary>Сегмент полосы: закрыто / долг / впереди.</summary>
    public CompetitionStateBucket Bucket => CompetitionRowStateRules.BucketOf(State);
}

/// <summary>
/// Счётчики состояний одной ячейки (сезон, месяц или весь список под фильтрами).
/// <paramref name="ClosedNothingToPull"/> — часть <paramref name="Closed"/>: закрыто без импорта
/// (скрыто, «∅», «✓ имп.» вручную). Из неё же собирается прежний счётчик чипа «затянуто+нечего/всего»
/// — один источник, двух подсчётов нет.
/// </summary>
public sealed record CompetitionStateCounts(int Ahead, int Running, int ReadyToPull, int WaitingProtocol, int Closed,
    int ClosedNothingToPull = 0)
{
    public static readonly CompetitionStateCounts Zero = new(0, 0, 0, 0, 0);

    /// <summary>Закрыто импортом (строка в базе) — «затянуто» прежнего счётчика.</summary>
    public int ClosedInDb => Closed - ClosedNothingToPull;

    public int Total => Ahead + Running + ReadyToPull + WaitingProtocol + Closed;

    /// <summary>Долг = ждёт затягивания + ждём протокол.</summary>
    public int Debt => ReadyToPull + WaitingProtocol;

    /// <summary>«Впереди» в полосах = впереди + идёт.</summary>
    public int AheadTotal => Ahead + Running;

    public int Of(CompetitionStateBucket bucket) => bucket switch
    {
        CompetitionStateBucket.Closed => Closed,
        CompetitionStateBucket.Debt => Debt,
        _ => AheadTotal,
    };

    public CompetitionStateCounts Add(CompetitionRowState state) => state switch
    {
        CompetitionRowState.Ahead => this with { Ahead = Ahead + 1 },
        CompetitionRowState.Running => this with { Running = Running + 1 },
        CompetitionRowState.ReadyToPull => this with { ReadyToPull = ReadyToPull + 1 },
        CompetitionRowState.WaitingProtocol => this with { WaitingProtocol = WaitingProtocol + 1 },
        _ => this with { Closed = Closed + 1 },
    };

    /// <summary>Строка целиком: состояние и, у закрытого, «закрыто без импорта».</summary>
    public CompetitionStateCounts Add(CompetitionRowStatus status)
    {
        var next = Add(status.State);
        return status is { State: CompetitionRowState.Closed, NothingToPull: true }
            ? next with { ClosedNothingToPull = ClosedNothingToPull + 1 }
            : next;
    }

    public static CompetitionStateCounts operator +(CompetitionStateCounts a, CompetitionStateCounts b) =>
        new(a.Ahead + b.Ahead, a.Running + b.Running, a.ReadyToPull + b.ReadyToPull,
            a.WaitingProtocol + b.WaitingProtocol, a.Closed + b.Closed, a.ClosedNothingToPull + b.ClosedNothingToPull);

    public static CompetitionStateCounts Of(IEnumerable<CompetitionRowStatus?> statuses) =>
        statuses.Aggregate(Zero, (acc, s) => s is null ? acc : acc.Add(s));
}

/// <summary>Блок дня группы «Ждут затягивания»: день (последний день старта) и его строки.</summary>
public sealed record ReadyToPullDay<T>(DateOnly Day, IReadOnlyList<T> Rows);

public static class CompetitionRowStateRules
{
    /// <summary>Месяцы в порядке сезона (сентябрь → август) — номера 1–12 для полосы месяцев.</summary>
    public static readonly IReadOnlyList<int> SeasonMonthOrder = [9, 10, 11, 12, 1, 2, 3, 4, 5, 6, 7, 8];

    /// <summary>
    /// Через сколько дней пропажи с сайта федерации (считая от <c>MissingSince</c>, а не от даты
    /// старта) прошедший старт в долге получает пометку «пропал N дн. — скрыть?» (решение Влада
    /// 23.11.2026). Автоматически ничего не закрывается — скрывает человек кнопкой «Скрыть».
    /// </summary>
    public const int MissingSuggestHideDays = 7;

    /// <summary>«Сегодня» по времени Израиля — граница «впереди / идёт / прошёл».</summary>
    public static DateOnly IsraelToday(DateTimeOffset utcNow) =>
        DateOnly.FromDateTime(IsraelTime.ToLocal(utcNow.UtcDateTime));

    public static CompetitionStateBucket BucketOf(CompetitionRowState state) => state switch
    {
        CompetitionRowState.Closed => CompetitionStateBucket.Closed,
        CompetitionRowState.ReadyToPull or CompetitionRowState.WaitingProtocol => CompetitionStateBucket.Debt,
        _ => CompetitionStateBucket.Ahead,
    };

    /// <summary>
    /// Состояние строки на день <paramref name="today"/> (местная дата Израиля).
    ///
    /// Порядок:
    /// 1. Скрыто, «∅» → закрыто (серое). Строка базы → закрыто, кроме видимой отметки
    ///    «результаты неполные»: такая проходит по датам дальше, как строка с сайта.
    ///    И кроме многодневки, затянутой по дням, пока её последний день (по сайту) не прошёл:
    ///    это «идёт», после — закрыто (решение Влада 23.11.2026).
    /// 2. До первого дня → впереди; с первого по последний → идёт («день N из M»).
    /// 3. Прошёл: вердикт results-auto, если он есть (он свежее и точнее ошибки забора);
    ///    без вердикта — «не опубликованы» → ждём протокол, иначе → ждёт затягивания.
    /// Строка без даты (не читается) — ждёт затягивания: «прошёл» безопаснее «впереди», её видно в долге.
    /// Пропажа с сайта состояние не меняет (прошедший старт — долг), только даёт пометку
    /// <see cref="CompetitionRowStatus.SuggestHide"/>.
    /// </summary>
    public static CompetitionRowStatus Evaluate(CompetitionRowStateInput input, DateOnly today)
    {
        var status = EvaluateState(input, today);
        if (input.MissingSinceDay is not { } gone) return status;
        var days = Math.Max(0, today.DayNumber - gone.DayNumber);
        return status with
        {
            MissingDays = days,
            SuggestHide = status.Bucket == CompetitionStateBucket.Debt && days >= MissingSuggestHideDays,
        };
    }

    private static CompetitionRowStatus EvaluateState(CompetitionRowStateInput input, DateOnly today)
    {
        var first = input.FirstDay;
        var last = input.LastDay is { } l && first is { } f && l >= f ? l : first;
        var dayCount = first is { } f2 && last is { } l2 ? l2.DayNumber - f2.DayNumber + 1 : 1;

        CompetitionRowStatus Make(CompetitionRowState s, WaitingProtocolReason? reason = null,
            int? runningDay = null, bool needsYou = false, bool parseFailed = false, bool nothing = false) =>
            new(s, reason, runningDay, dayCount, needsYou, parseFailed, nothing, first, last);

        switch (input.Stage)
        {
            case CompetitionStage.Ignored:
                return Make(CompetitionRowState.Closed, nothing: true);
            case CompetitionStage.Imported or CompetitionStage.DbOnly when !input.PartialResults:
                // Многодневку тянут по дням (путь Б в pull-to-prod): пока последний день по сайту
                // не прошёл, затянутая часть — это «идёт», а не «закрыто» (решение Влада 23.11.2026).
                return dayCount > 1 && first is { } fd && last is { } ld && today >= fd && today <= ld
                    ? Make(CompetitionRowState.Running, runningDay: today.DayNumber - fd.DayNumber + 1)
                    : Make(CompetitionRowState.Closed);
            case CompetitionStage.OnSite when input.NoProtocolMark || input.MarkedImported:
                return Make(CompetitionRowState.Closed, nothing: true);
        }

        if (first is { } start && today < start)
            return Make(CompetitionRowState.Ahead);
        if (first is { } s0 && last is { } end && today <= end)
            return Make(CompetitionRowState.Running, runningDay: today.DayNumber - s0.DayNumber + 1);

        // Затянутое с неполным протоколом: ждём полный, вердикт задачи к нему не относится
        // (results-auto смотрит только незатянутые).
        if (input.Stage is CompetitionStage.Imported or CompetitionStage.DbOnly)
            return Make(CompetitionRowState.WaitingProtocol, WaitingProtocolReason.Incomplete);

        return input.AutoPullVerdict switch
        {
            ResultsAutoVerdicts.WouldTake => Make(CompetitionRowState.ReadyToPull),
            ResultsAutoVerdicts.Blocked => Make(CompetitionRowState.ReadyToPull, needsYou: true),
            ResultsAutoVerdicts.NotFinished => Make(CompetitionRowState.WaitingProtocol, WaitingProtocolReason.Incomplete),
            ResultsAutoVerdicts.Empty => Make(CompetitionRowState.WaitingProtocol, WaitingProtocolReason.NoProtocol),
            ResultsAutoVerdicts.Failed => Make(CompetitionRowState.WaitingProtocol, WaitingProtocolReason.ParseFailed, parseFailed: true),
            _ when input.ResultsNotPublished => Make(CompetitionRowState.WaitingProtocol, WaitingProtocolReason.NoProtocol),
            _ => Make(CompetitionRowState.ReadyToPull),
        };
    }

    /// <summary>
    /// Группа «Ждут затягивания» по дням: блок на последний день старта, от свежего к старому;
    /// внутри блока порядок исходного списка. Строки других состояний пропускаются.
    /// </summary>
    public static IReadOnlyList<ReadyToPullDay<T>> GroupReadyToPullByDay<T>(
        IEnumerable<T> rows, Func<T, CompetitionRowStatus?> status) =>
        rows.Select(r => (Row: r, Status: status(r)))
            .Where(x => x.Status is { State: CompetitionRowState.ReadyToPull })
            .GroupBy(x => x.Status!.LastDay ?? DateOnly.MinValue)
            .OrderByDescending(g => g.Key)
            .Select(g => new ReadyToPullDay<T>(g.Key, g.Select(x => x.Row).ToList()))
            .ToList();
}
