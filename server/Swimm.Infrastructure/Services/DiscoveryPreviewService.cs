using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Infrastructure.Repositories;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// «Затянуть» строку входящих: PDF из loglig → парсер → превью рекордов и клубного зачёта.
/// Переехало сюда из <c>DiscoveryAdminController</c>, чтобы пакетный забор звал ТО ЖЕ САМОЕ,
/// а не свою копию (docs/plans/bulk-pull-plan.md, Б2).
/// </summary>
public class DiscoveryPreviewService : IDiscoveryPreviewService
{
    /// <summary>
    /// Сколько живёт отложенный разбор. Было 15 минут (одиночный поток: затянул — сразу
    /// импортировал), стало 60: в пакете два десятка строк тянутся минутами, и первый разбор
    /// не должен протухнуть, пока идёт последний.
    /// </summary>
    public TimeSpan EntryLifetime => TimeSpan.FromMinutes(60);

    private readonly ICompetitionDiscoveryService _discovery;
    private readonly ICompetitionDiscoveryProvider _provider;
    private readonly IResultSourceProvider _sourceProvider;
    private readonly IImportService _import;
    private readonly IImportRecordPreviewService _recordPreview;
    private readonly IOfficialClubStandingService _clubStandings;
    private readonly IRegulationFetchService _regulations;
    private readonly IMemoryCache _cache;
    private readonly ILogger<DiscoveryPreviewService> _logger;

    public DiscoveryPreviewService(
        ICompetitionDiscoveryService discovery,
        ICompetitionDiscoveryProvider provider,
        IResultSourceProvider sourceProvider,
        IImportService import,
        IImportRecordPreviewService recordPreview,
        IOfficialClubStandingService clubStandings,
        IRegulationFetchService regulations,
        IMemoryCache cache,
        ILogger<DiscoveryPreviewService> logger)
    {
        _discovery = discovery;
        _provider = provider;
        _sourceProvider = sourceProvider;
        _import = import;
        _recordPreview = recordPreview;
        _clubStandings = clubStandings;
        _regulations = regulations;
        _cache = cache;
        _logger = logger;
    }

    public DiscoveryPreviewEntry? GetEntry(Guid previewId) =>
        _cache.TryGetValue(CacheKey(previewId), out DiscoveryPreviewEntry? entry) ? entry : null;

    public void RemoveEntry(Guid previewId) => _cache.Remove(CacheKey(previewId));

    /// <inheritdoc />
    public async Task<DiscoveryPreviewResult> PreviewAsync(int discoveredId, CancellationToken ct = default)
    {
        var (pdfHe, fileNameHe, errorHe) = await FetchPdfAsync(discoveredId, "he", refreshIfMissing: true, ct);
        var (pdfEn, fileNameEn, _) = await FetchPdfAsync(discoveredId, "en", refreshIfMissing: false, ct);
        if (pdfHe is null && pdfEn is null)
            return DiscoveryPreviewResult.Failed(errorHe ?? "Протокол недоступен");

        // Основной файл — HE (канонические имена); EN вторым даёт LastNameEn/FirstNameEn.
        // Если HE недоступен (маловероятно) — парсим EN-only.
        var language = pdfHe != null ? "he" : "en";
        var primary = pdfHe ?? pdfEn!;
        var primaryName = pdfHe != null ? fileNameHe : fileNameEn;
        var languages = new List<string>();
        if (pdfHe != null) languages.Add("he");
        if (pdfEn != null) languages.Add("en");

        ParsedCompetition parsed;
        try
        {
            using var ms = new MemoryStream(primary);
            using var msEn = pdfHe != null && pdfEn != null ? new MemoryStream(pdfEn) : null;
            parsed = await _sourceProvider.ParseAsync(new ResultSourceRequest(
                ms, primaryName, "IsrOrg",
                IsAward: false, PoolType: null,
                SecondaryStream: msEn, SecondaryFileName: msEn != null ? fileNameEn : null,
                ExtraFiles: null,
                Country: null, Language: language));
        }
        catch (InvalidOperationException ex) when (pdfHe != null && pdfEn != null)
        {
            // Пара не склеилась (разный порядок записей и т.п.) — деградируем до HE-only,
            // EN-имена добираются потом кнопкой «Синхр. языки» после починки.
            _logger.LogWarning(ex, "Discovery: двуязычная пара не склеилась (id={Id}), парсим HE-only", discoveredId);
            languages.Remove("en");
            using var ms = new MemoryStream(pdfHe);
            try
            {
                parsed = await _sourceProvider.ParseAsync(new ResultSourceRequest(
                    ms, fileNameHe, "IsrOrg",
                    IsAward: false, PoolType: null,
                    SecondaryStream: null, SecondaryFileName: null, ExtraFiles: null,
                    Country: null, Language: "he"));
            }
            catch (InvalidOperationException ex2)
            {
                return DiscoveryPreviewResult.Failed(ex2.Message);
            }
        }
        catch (InvalidOperationException ex)
        {
            // «No competitions found in PDF» = у соревнования нет протокола (страница пустая).
            // Это не сбой, который стоит повторить, а факт «тянуть нечего» — помечаем строку,
            // чтобы её не пробовали затянуть снова и снова.
            if (LooksLikeEmptySource(ex.Message))
                await _discovery.SetEmptySourceAsync(discoveredId, true, "auto", ct);
            return DiscoveryPreviewResult.Failed(ex.Message);
        }

        if (parsed.ResultCount == 0)
        {
            await _discovery.SetEmptySourceAsync(discoveredId, true, "auto", ct);
            return DiscoveryPreviewResult.Failed(
                "Парсер не распознал ни одного результата — формат протокола изменился? (B4)");
        }

        // Разобралось — значит протокол всё-таки есть: снимаем прежнюю пометку «пусто»
        // (файл могли выложить позже, и строка не должна оставаться зачёркнутой навсегда).
        await _discovery.SetEmptySourceAsync(discoveredId, false, "auto", ct);
        await _discovery.AddLanguagesAsync(discoveredId, languages, ct);

        // Официальный клубный зачёт: есть ли он и по какой шкале. Кладём В КЭШ вместе с превью —
        // из него потом заводится правило кнопкой, и второй поход в loglig (десяток запросов
        // ради той же шкалы) был бы лишним.
        var standingProbe = await ProbeClubStandingAsync(discoveredId, ct);

        var existingMatches = await _import.FindExistingCompetitionsAsync(parsed.Competitions);
        var existingCompetitionId = existingMatches.FirstOrDefault(m => m.ExistingCompetitionId != null)?.ExistingCompetitionId;

        // Сколько рекордов побьёт файл (Б2). Считается ДО «Применить»: настоящий рекорд —
        // событие редкое, а десяток разом почти всегда значит, что протокол разобрался неверно.
        var recordPreview = await _recordPreview.AnalyzeAsync(parsed.ResultsJson, ct);

        var previewId = Guid.NewGuid();
        _cache.Set(CacheKey(previewId),
            new DiscoveryPreviewEntry(parsed, primaryName, discoveredId, standingProbe, recordPreview),
            EntryLifetime);

        var flags = await SuggestFlagsAsync(discoveredId, parsed, standingProbe, ct);

        return new DiscoveryPreviewResult(
            previewId, parsed, languages, existingCompetitionId, existingMatches, recordPreview,
            standingProbe, flags);
    }

    /// <summary>
    /// Что предложить проставить соревнованию: медали и чемпионат — из регламента (его мы
    /// теперь качаем сами), чемпионат ещё и по названию, мастерс — из разобранного файла,
    /// «зачёт не ведётся» — из пробы loglig, бассейн — из парсера.
    ///
    /// Раньше всё это ставилось руками ПОСЛЕ импорта, в панели строки, и про половину
    /// забывали. Сбой любого источника не роняет превью — флаг просто останется снятым.
    /// </summary>
    private async Task<CompetitionFlagSuggestion> SuggestFlagsAsync(
        int discoveredId, ParsedCompetition parsed, OfficialClubStandingProbe? standing, CancellationToken ct)
    {
        var row = (await _discovery.GetAllAsync(ct)).FirstOrDefault(d => d.Id == discoveredId);
        var reasons = new Dictionary<string, string>();

        RegulationFetchDto? regulation = null;
        if (row?.LogligId is int logligId)
        {
            try
            {
                regulation = await _regulations.FetchAsync(logligId, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Discovery: регламент для строки {Id} не прочитался", discoveredId);
            }
        }

        var analysis = regulation?.Analysis;
        var isAward = analysis?.HasMedals ?? false;
        if (isAward) reasons["isAward"] = Quote(analysis!, "medals", "регламент упоминает медали");

        // Чемпионат: имя — тот же предикат, что у фильтра списка; регламент — второй источник.
        var championshipByName = CompetitionAdminRepository.IsChampionship(row?.Name);
        var isChampionship = championshipByName || (analysis?.IsChampionship ?? false);
        if (isChampionship)
            reasons["isChampionship"] = championshipByName
                ? "в названии «אליפות … ישראל»"
                : Quote(analysis!, "championship", "регламент чемпионата Израиля");

        // Мастерс: признак стоит у строк файла — парсер уже разметил их при разборе.
        var isMasters = parsed.ResultsJson.Contains("\"is_masters\":true", StringComparison.OrdinalIgnoreCase);
        if (isMasters) reasons["isMasters"] = "в файле есть мастерс-заплывы";

        // «Зачёт не ведётся» предлагаем, только когда мы ТОЧНО знаем, что его нет: null —
        // это «не проверяли» (нет loglig-id, сайт лёг), и снимать по нему галочку нельзя.
        var clubPointsDisabled = standing is { HasStanding: false };
        if (clubPointsDisabled) reasons["clubPointsDisabled"] = "на loglig клубного зачёта нет";

        var poolType = parsed.Competitions.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c.PoolType))?.PoolType;
        if (!string.IsNullOrWhiteSpace(poolType)) reasons["poolType"] = "распознано парсером";

        return new CompetitionFlagSuggestion(
            isAward, isChampionship, isMasters, clubPointsDisabled, poolType, regulation?.Url, reasons);
    }

    /// <summary>Цитата регламента по флагу — основание, которое видит админ.</summary>
    private static string Quote(RegulationAnalysisDto analysis, string flag, string fallback)
    {
        var finding = analysis.Findings.FirstOrDefault(f => f.Flag == flag);
        return finding is null ? fallback : $"регламент: «{finding.Quote}»";
    }

    /// <summary>
    /// Официальный клубный зачёт соревнования. Сбой проверки превью не роняет: затянуть
    /// протокол важнее, чем узнать про зачёт — вернём «не проверено».
    /// </summary>
    private async Task<OfficialClubStandingProbe?> ProbeClubStandingAsync(int discoveredId, CancellationToken ct)
    {
        var row = (await _discovery.GetAllAsync(ct)).FirstOrDefault(d => d.Id == discoveredId);
        if (row?.LogligId is not int logligId) return null;

        try
        {
            return await _clubStandings.ProbeAsync(logligId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Discovery: не удалось проверить клубный зачёт для строки {Id}", discoveredId);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<DiscoveryProtocolPdf> FetchProtocolAsync(
        int discoveredId, string language, bool refreshIfMissing, CancellationToken ct = default)
    {
        var (pdf, fileName, error) = await FetchPdfAsync(discoveredId, language, refreshIfMissing, ct);
        return new DiscoveryProtocolPdf(pdf, fileName, error);
    }

    private async Task<(byte[]? pdf, string fileName, string? error)> FetchPdfAsync(
        int id, string language, bool refreshIfMissing, CancellationToken ct)
    {
        var all = await _discovery.GetAllAsync(ct);
        var row = all.FirstOrDefault(d => d.Id == id);
        if (row is null) return (null, "", "Запись не найдена");

        var logligId = row.LogligId;
        if (logligId is null)
        {
            if (!refreshIfMissing)
                return (null, "", "Детали не загружены — нажмите «Затянуть» (нет loglig-id).");

            // Детали могли ещё не загружаться — пробуем один раз.
            var refreshed = await _discovery.RefreshDetailsAsync(id, ct);
            logligId = refreshed?.LogligId;
            if (logligId is null)
                return (null, "", refreshed?.LastError ?? "Результаты не опубликованы (нет loglig-id).");
        }

        var culture = language == "en" ? "en-US" : "he-IL";
        try
        {
            var pdf = await _provider.FetchResultsPdfAsync(logligId.Value, culture, ct);
            return (pdf, $"isrorg-{row.OrgCompId}-loglig-{logligId}-{language}.pdf", null);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Discovery: не удалось скачать PDF logligId={LogligId}", logligId);
            return (null, "", ex.Message);
        }
    }

    /// <summary>
    /// Сообщения парсера, означающие «в файле ничего нет». Держим списком, а не подстрокой
    /// «not found»: иначе под пометку попали бы сетевые и форматные сбои, которые надо повторять.
    /// </summary>
    private static bool LooksLikeEmptySource(string message) =>
        message.Contains("No competitions found", StringComparison.OrdinalIgnoreCase)
        || message.Contains("0 lines extracted", StringComparison.OrdinalIgnoreCase);

    private static string CacheKey(Guid previewId) => $"discovery-preview:{previewId}";
}
