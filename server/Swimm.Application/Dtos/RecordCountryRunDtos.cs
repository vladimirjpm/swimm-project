namespace Swimm.Application.Dtos;

/// <summary>Состояние прогона рекордов по странам (этап 11.1.2).</summary>
public enum RecordCountryRunState
{
    Queued,
    Running,
    Completed,
    Failed,
}

/// <summary>
/// Запрос на прогон. <paramref name="Codes"/> пуст или null — все страны источника.
///
/// Он же «повторить упавшие»: отдельной кнопки-эндпоинта нет, админка шлёт сюда коды из
/// <see cref="RecordCountryRunStatus.Failed"/> прошлого прогона.
/// </summary>
public sealed record RecordCountryRunRequest(IReadOnlyList<string>? Codes = null);

/// <summary>Страна, которая не скачалась: прогон её пропустил и пошёл дальше.</summary>
public sealed record RecordCountryRunFailure(string Code, string Error);

/// <summary>
/// Статус прогона — его опрашивает админка (очередь в памяти, как у импорта протоколов).
/// Мутабельный класс по образцу <see cref="ImportJobStatus"/>: очередь отдаёт ссылку, а
/// фоновая задача правит поля по ходу дела.
///
/// ⚠ Рестарт API прогон теряет — это принято сознательно (план 11.1.2): прогон идёт час-два,
/// хранить его в БД ради выживания при рестарте дороже, чем запустить заново.
/// </summary>
public sealed class RecordCountryRunStatus
{
    public Guid RunId { get; init; }
    public RecordCountryRunState State { get; set; }
    public DateTimeOffset QueuedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>Сколько стран в прогоне (уже без Израиля и без неизвестных кодов).</summary>
    public int Total { get; set; }

    /// <summary>Сколько стран пройдено — и успешных, и упавших.</summary>
    public int Done { get; set; }

    /// <summary>Какую страну качаем прямо сейчас; null — ещё не начали или уже закончили.</summary>
    public string? CurrentCode { get; set; }

    /// <summary>Строк набрано за прогон (включая мировые, если они скачались).</summary>
    public int RowsFetched { get; set; }

    /// <summary>Скачались ли мировые рекорды: они берутся один раз на прогон, а не на страну.</summary>
    public bool WorldFetched { get; set; }

    /// <summary>
    /// Пропущен ли Израиль. Пропускается ВСЕГДА (инвариант 2 плана): на
    /// <c>country/ISR/open</c> федерация обязана применяться после World Aquatics, иначе
    /// молча откатятся более свежие рекорды. Поле — чтобы это было видно, а не подразумевалось.
    /// </summary>
    public bool IsraelSkipped { get; set; }

    public IReadOnlyList<RecordCountryRunFailure> Failed { get; set; } = [];

    /// <summary>Сколько строк источника прогон не принял (чужой или пустой <c>NF Code</c>).</summary>
    public int MismatchCount { get; set; }

    /// <summary>
    /// Первые несовпадения — показать админу. Полный список не держим: на 235 странах он
    /// может быть длиннее самого диффа, а смысл у него один — «посмотри, что там у источника».
    /// </summary>
    public IReadOnlyList<RecordCountryMismatchDto> Mismatches { get; set; } = [];

    /// <summary>Итог прогона: дифф, который ждёт Apply. null, пока прогон не закончился.</summary>
    public RecordDiffResult? Diff { get; set; }

    public string? Error { get; set; }
}

/// <summary>Правила прогона, которые должны быть видны и прикладному слою, и админке.</summary>
public static class RecordCountryRun
{
    /// <summary>
    /// Страна, которую прогон по странам не трогает никогда (инвариант 2 плана 11.1.2):
    /// на <c>country/ISR/open</c> федерация обязана применяться ПОСЛЕ World Aquatics (И-13),
    /// иначе батч молча откатит более свежие израильские рекорды. Израиль обновляет только
    /// <c>--records-refresh</c>, где порядок источников зашит.
    /// </summary>
    public const string SkippedCode = "ISR";
}
