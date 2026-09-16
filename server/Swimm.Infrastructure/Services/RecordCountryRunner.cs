using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Прогон «национальные рекорды по странам» (этап 11.1.2): мировые один раз, потом NR каждой
/// страны, в конце — один дифф на весь прогон. Сама фоновая обвязка (очередь, статусы) —
/// в <see cref="RecordCountryRunQueue"/>; здесь только ход работ, чтобы его можно было
/// прогнать тестом без хоста.
///
/// Что здесь принципиально:
/// <list type="bullet">
/// <item><b>Израиль пропускается всегда</b> (инвариант 2 плана): на <c>country/ISR/open</c>
/// федерация обязана применяться ПОСЛЕ World Aquatics (И-13), иначе батч молча откатит
/// более свежие рекорды федерации. Израиль обновляет только <c>--records-refresh</c>.</item>
/// <item><b>Упавшая страна не роняет прогон.</b> 235 стран по паре файлов — что-нибудь
/// обязательно отвалится; список упавших виден в статусе, повтор — новый прогон с этими же
/// кодами.</item>
/// <item><b>Дифф строится один, в конце.</b> Он живёт до Apply (12 часов), а не десять минут
/// превью: сам прогон идёт час-два.</item>
/// <item><b>Удаления нет.</b> Apply только upsert — инвариант 1 плана (age/masters
/// израильские) держится сам собой, и «нет в источнике» остаётся справочной цифрой.</item>
/// </list>
/// </summary>
public sealed class RecordCountryRunner
{
    /// <summary>Пауза между странами — вежливость к источнику (как 2 с в Discovery).</summary>
    private static readonly TimeSpan DefaultPause = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Сколько живёт дифф прогона. Прогон по всем странам идёт час-два, и 10 минут превью
    /// закончились бы раньше, чем он сам.
    /// </summary>
    private static readonly TimeSpan DiffTtl = TimeSpan.FromHours(12);

    /// <summary>
    /// Сколько непринятых строк показываем. Полный список на 235 странах может оказаться
    /// длиннее диффа, а смысл у него один — «посмотри, что там у источника».
    /// </summary>
    private const int MismatchSample = 200;

    private readonly IRecordCountriesProvider _countries;
    private readonly IRecordCountryFetcher _fetcher;
    private readonly IRecordDiffService _diff;
    private readonly TimeSpan _pause;

    /// <param name="pause">Пауза между странами; в тестах — ноль.</param>
    public RecordCountryRunner(
        IRecordCountriesProvider countries,
        IRecordCountryFetcher fetcher,
        IRecordDiffService diff,
        TimeSpan? pause = null)
    {
        _countries = countries;
        _fetcher = fetcher;
        _diff = diff;
        _pause = pause ?? DefaultPause;
    }

    /// <summary>
    /// Ход прогона. Статус правится по мере работы — его в это же время опрашивает админка.
    /// Бросает, только если прогон целиком бессмысленен (список стран не скачался, ни одной
    /// строки не набралось) или его отменили.
    /// </summary>
    public async Task RunAsync(
        RecordCountryRunStatus status, IReadOnlyList<string>? codes, CancellationToken ct)
    {
        var all = await _countries.GetCountriesAsync(ct);
        var failed = new List<RecordCountryRunFailure>();
        var selected = Select(all, codes, failed);

        // Инвариант 2: Израиль из прогона вылетает даже если его попросили явно.
        var withIsrael = selected.Count;
        selected = selected
            .Where(c => !string.Equals(c.Code, RecordCountryRun.SkippedCode, StringComparison.OrdinalIgnoreCase))
            .ToList();
        status.IsraelSkipped = selected.Count != withIsrael;

        status.Total = selected.Count;
        status.Failed = failed.ToArray();

        var rows = new List<ParsedRecordDto>();
        var mismatches = new List<RecordCountryMismatchDto>();

        // Мировые — один раз на прогон. Не скачались: прогон продолжается, а сторож
        // правдоподобия возьмёт эталон из базы (RecordDiffService.WorldReferenceAsync).
        try
        {
            rows.AddRange(await _fetcher.FetchWorldAsync(ct));
            status.WorldFetched = true;
            status.RowsFetched = rows.Count;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            failed.Add(new RecordCountryRunFailure("WR", ex.Message));
            status.Failed = failed.ToArray();
        }

        var first = true;
        foreach (var country in selected)
        {
            ct.ThrowIfCancellationRequested();

            if (!first) await Task.Delay(_pause, ct);
            first = false;

            status.CurrentCode = country.Code;
            try
            {
                var fetched = await _fetcher.FetchAsync(country, ct);
                rows.AddRange(fetched.Records);

                status.MismatchCount += fetched.Mismatches.Count;
                foreach (var m in fetched.Mismatches)
                {
                    if (mismatches.Count >= MismatchSample) break;
                    mismatches.Add(m);
                }
                status.Mismatches = mismatches.ToArray();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed.Add(new RecordCountryRunFailure(country.Code, ex.Message));
                status.Failed = failed.ToArray();
            }

            status.Done++;
            status.RowsFetched = rows.Count;
        }

        status.CurrentCode = null;

        if (rows.Count == 0)
            throw new InvalidOperationException(
                failed.Count > 0
                    ? $"Прогон не дал ни одной строки: не скачалась ни одна страна из {status.Total}."
                    : "Прогон не дал ни одной строки — проверьте список стран.");

        status.Diff = await _diff.BuildDiffAsync(_fetcher.Source, rows, DiffTtl, ct);
    }

    /// <summary>
    /// Пустой список кодов — все страны источника. Код, которого у источника нет, — не повод
    /// отменять прогон: он попадает в упавшие и виден админу вместе с остальными.
    /// </summary>
    private static List<RecordCountryDto> Select(
        IReadOnlyList<RecordCountryDto> all, IReadOnlyList<string>? codes, List<RecordCountryRunFailure> failed)
    {
        if (codes is not { Count: > 0 }) return all.ToList();

        var byCode = all.ToDictionary(c => c.Code, StringComparer.OrdinalIgnoreCase);
        var selected = new List<RecordCountryDto>();

        foreach (var code in codes.Select(c => (c ?? "").Trim().ToUpperInvariant()).Distinct())
        {
            if (code.Length == 0) continue;

            if (byCode.TryGetValue(code, out var country)) selected.Add(country);
            else failed.Add(new RecordCountryRunFailure(code, "нет в списке стран источника"));
        }

        return selected;
    }
}
