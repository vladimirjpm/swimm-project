using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Validation;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Сверка подозрительных заплывов превью с карточкой пловца на loglig.
///
/// Логика простая и держится на одном факте: протокол мы качаем С LOGLIG, значит время из
/// него обязано быть и в карточке пловца. Совпало — рекорд настоящий, помечать нечего; в
/// карточке время ХУЖЕ — почти наверняка разбор поехал (эталон: 00:32.59 на 100 баттерфляем).
///
/// Пловца превью в БД может не быть вовсе: соревнование ещё не импортировано. Тогда ни
/// проверять, ни привязывать нечего — так и говорим.
/// </summary>
public class PreviewRecordCheckService : IPreviewRecordCheckService
{
    private readonly SwimmDbContext _db;
    private readonly IDiscoveryPreviewService _previews;
    private readonly ICompetitionDiscoveryService _discovery;
    private readonly ILogligClient _loglig;
    private readonly IMemoryCache _cache;

    public PreviewRecordCheckService(
        SwimmDbContext db,
        IDiscoveryPreviewService previews,
        ICompetitionDiscoveryService discovery,
        ILogligClient loglig,
        IMemoryCache cache)
    {
        _db = db;
        _previews = previews;
        _discovery = discovery;
        _loglig = loglig;
        _cache = cache;
    }

    public async Task<IReadOnlyList<PreviewRecordCheckRow>> CheckAsync(
        Guid previewId, CancellationToken ct = default)
    {
        var entry = _previews.GetEntry(previewId);
        var rows = entry?.Records?.Rows;
        if (entry is null || rows is null || rows.Count == 0) return [];

        // Сезон ЭТОГО соревнования: карточка с сезоном из конфига показала бы другой год —
        // и ссылка «посмотреть глазами» вела бы не туда (пример Влада: seasonId=1605).
        var discoveredRow = (await _discovery.GetAllAsync(ct)).FirstOrDefault(d => d.Id == entry.DiscoveredId);
        var seasonId = discoveredRow?.LogligId is int compLogligId
            ? await _loglig.GetCompetitionSeasonIdAsync(compLogligId, ct)
            : null;

        // Один заплыв бьёт и open-, и возрастной рекорд — в превью это ДВЕ строки с одним
        // rowIndex. Проверять его дважды незачем.
        var swims = rows
            .GroupBy(r => r.RowIndex)
            .Select(g => g.First())
            .ToList();

        var swimmers = await LoadCandidatesAsync(swims, ct);

        // Id прямо из протокола: на странице заплыва loglig имя — ссылка на карточку. Это
        // работает и для пловца, которого в нашей базе ещё нет (соревнование не импортировано),
        // и избавляет от ручного ввода id для тех, кто есть, но не привязан.
        var fromProtocol = await LoadProtocolIdsAsync(discoveredRow?.LogligId, swims, ct);

        var cards = new Dictionary<int, LogligPlayerCard?>();
        var result = new List<PreviewRecordCheckRow>();

        foreach (var swim in swims)
        {
            var key = NameKey(swim.SwimmerName, swim.BirthYear);
            var matches = swimmers.TryGetValue(key, out var list) ? list : [];
            var protocolId = fromProtocol.TryGetValue(key, out var pid) ? pid : (int?)null;

            var swimmer = matches.Count == 1 ? matches[0] : null;
            var match = matches.Count switch
            {
                0 => PreviewSwimmerMatch.None,
                1 => PreviewSwimmerMatch.One,
                _ => PreviewSwimmerMatch.Many
            };

            // Тёзки: работать с конкретным пловцом нельзя — id уедет не тому. Но время
            // проверить всё равно можно, если протокол дал id именно этой строки.
            var logligId = swimmer?.LogligId ?? protocolId;

            if (logligId is not int id)
            {
                result.Add(new PreviewRecordCheckRow(
                    swim.RowIndex, swim.SwimmerName, match, swimmer?.Id, null, null, swimmer?.Gender,
                    RecordCheckVerdict.NotChecked, NoIdMessage(match, matches.Count)));
                continue;
            }

            if (!cards.TryGetValue(id, out var card))
            {
                card = await _loglig.GetPlayerCardAsync(id, seasonId, ct);
                cards[id] = card;
            }

            var url = _loglig.BuildPublicProfileUrl(id, seasonId, resultsTab: true);
            var (verdict, message) = Compare(card, swim);

            // Приписка о том, что делать с самим id: привязать, или ждать импорта, или выбрать
            // из тёзок. Без неё вердикт есть, а следующего шага не видно.
            var linkedInDb = swimmer?.LogligId != null;
            var suffix = (match, linkedInDb) switch
            {
                (PreviewSwimmerMatch.One, false) => " Id взят из протокола — можно привязать одной кнопкой.",
                (PreviewSwimmerMatch.None, _) => " Пловца ещё нет в базе — id проставится при импорте.",
                (PreviewSwimmerMatch.Many, _) => $" В базе {matches.Count} тёзки — привязку делайте в /Admin/Swimmers/Loglig.",
                _ => ""
            };

            result.Add(new PreviewRecordCheckRow(
                swim.RowIndex, swim.SwimmerName, match,
                match == PreviewSwimmerMatch.One ? swimmer!.Id : null,
                swimmer?.LogligId, url, swimmer?.Gender, verdict, message + suffix,
                SuggestedLogligId: swimmer?.LogligId is null ? protocolId : null));
        }

        return result;
    }

    /// <summary>Что писать, когда id не нашёлся ни в базе, ни в протоколе.</summary>
    private static string NoIdMessage(PreviewSwimmerMatch match, int matchCount) => match switch
    {
        PreviewSwimmerMatch.None =>
            "Пловца нет ни в базе, ни среди участников на loglig — проверить нечем.",
        PreviewSwimmerMatch.Many =>
            $"В базе {matchCount} тёзки с этим годом, а протокол id не дал — разбирайте в /Admin/Swimmers/Loglig.",
        _ =>
            "Пловец не привязан к loglig, и в протоколе его карточки нет — впишите id руками."
    };

    /// <summary>
    /// Loglig-id участников ЭТОГО соревнования по именам подозрительных строк. Обход заплывов
    /// платный (страница на заплыв), поэтому: ищем только нужных, останавливаемся, как только
    /// нашли всех, и держим результат в кэше — record-check зовут повторно (перезагрузка
    /// страницы, второй взгляд на ту же пачку).
    /// </summary>
    private async Task<Dictionary<string, int>> LoadProtocolIdsAsync(
        int? competitionLogligId, IReadOnlyList<ImportRecordPreviewRow> swims, CancellationToken ct)
    {
        if (competitionLogligId is not int compId) return [];

        var cacheKey = $"loglig-participants:{compId}:{swims.Count}";
        if (_cache.TryGetValue(cacheKey, out Dictionary<string, int>? cached) && cached != null)
            return cached;

        var wanted = swims.Select(s => LogligClient.ParticipantKey(s.SwimmerName, s.BirthYear)).ToHashSet();

        IReadOnlyList<LogligParticipant> participants;
        try
        {
            participants = await _loglig.GetCompetitionParticipantsAsync(compId, wanted, ct: ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Сбой обхода не должен ронять проверку: часть строк просто останется без id.
            return [];
        }

        // Сопоставляем НЕ по равенству ключей: на сайте имя бывает полнее («אליה מאשה גדול»
        // против «אליה גדול»). Одного подходящего кандидата берём, нескольких — нет.
        var candidates = participants
            .Select(p => ((IReadOnlyCollection<string>)LogligClient.NameTokens(p.FullName), p.BirthYear, p.LogligId))
            .ToList();

        var map = new Dictionary<string, int>();
        foreach (var swim in swims)
        {
            var key = NameKey(swim.SwimmerName, swim.BirthYear);
            if (map.ContainsKey(key)) continue;

            var found = TokenNameMatcher.ResolveSingle(
                candidates, LogligClient.NameTokens(swim.SwimmerName), swim.BirthYear);
            if (found != 0) map[key] = found;
        }

        _cache.Set(cacheKey, map, TimeSpan.FromMinutes(30));
        return map;
    }

    /// <summary>Итог сверки одного заплыва с карточкой. Сама логика — чистая функция
    /// <see cref="RecordCheckComparer"/>, здесь только «карточки нет».</summary>
    private static (RecordCheckVerdict Verdict, string Message) Compare(
        LogligPlayerCard? card, ImportRecordPreviewRow swim)
        => card is null
            ? (RecordCheckVerdict.NotChecked, "Карточка loglig недоступна — проверьте по ссылке руками.")
            : RecordCheckComparer.Compare(card.Results, swim.StyleName, swim.Distance, swim.PoolType, swim.Time);

    /// <summary>
    /// Кандидаты из БД по годам рождения строк превью. Грузим узко (по годам), а сопоставляем
    /// в памяти: имя надо нормализовать (иврит, гереш, порядок токенов), а такое сравнение
    /// SQL не выразит.
    /// </summary>
    private async Task<Dictionary<string, List<SwimmerRef>>> LoadCandidatesAsync(
        IReadOnlyList<ImportRecordPreviewRow> swims, CancellationToken ct)
    {
        var years = swims.Where(s => s.BirthYear.HasValue).Select(s => s.BirthYear!.Value).Distinct().ToList();
        if (years.Count == 0) return [];

        var candidates = await _db.Swimmers.AsNoTracking()
            .Where(s => years.Contains(s.BirthYear))
            .Select(s => new SwimmerRef(s.Id, s.LastName, s.FirstName, s.BirthYear, s.Gender, s.LogligId))
            .ToListAsync(ct);

        var map = new Dictionary<string, List<SwimmerRef>>();
        foreach (var s in candidates)
        {
            var key = NameKey($"{s.FirstName} {s.LastName}", s.BirthYear);
            if (!map.TryGetValue(key, out var list)) map[key] = list = [];
            list.Add(s);
        }

        return map;
    }

    /// <summary>
    /// Ключ сопоставления: нормализованный НАБОР токенов имени + год рождения. Набор, а не
    /// строка, потому что порядок «имя фамилия» в протоколе и в БД не совпадает
    /// (та же причина, что у <see cref="LogligSwimmerNameResolver"/>). Нормализация — общая
    /// с дедупом (финальные буквы, гереш), своей копии не заводим.
    /// </summary>
    private static string NameKey(string fullName, int? birthYear)
    {
        var tokens = SwimmerDedupService.Normalize(fullName)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .OrderBy(t => t, StringComparer.Ordinal);

        return string.Join('|', tokens) + "#" + (birthYear?.ToString() ?? "?");
    }

    private sealed record SwimmerRef(int Id, string LastName, string FirstName, int BirthYear, string? Gender, int? LogligId);
}
