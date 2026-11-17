using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;

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
/// <item><b>Упавшая страна не роняет прогон.</b> 215 стран по шесть запросов — что-нибудь
/// обязательно отвалится; список упавших виден в статусе.</item>
/// <item><b>Скачанное не теряется</b> (И-68). Каждая страна сразу после скачивания ложится на
/// диск (<see cref="IRecordCountryRunProgress"/>). Следующий прогон по всем странам — после
/// обрыва процесса или с упавшими — берёт уже скачанное оттуда и ходит в источник только за
/// остальным. Прошли все без сбоев — сохранённое стирается, и следующий прогон снова полный.</item>
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
    private readonly IRecordRunArchive? _archive;
    private readonly IRecordCountryRunProgress? _progress;
    private readonly TimeSpan _pause;

    /// <param name="archive">
    /// Куда сложить выгрузку разобранных строк. null — прогон не архивируется (так и в тестах,
    /// которым диск не нужен).
    /// </param>
    /// <param name="pause">Пауза между странами; в тестах — ноль.</param>
    /// <param name="progress">
    /// Куда сохранять скачанное по ходу прогона, чтобы оборванный прогон продолжился, а не начался
    /// заново (И-68). null — не сохраняем.
    /// </param>
    public RecordCountryRunner(
        IRecordCountriesProvider countries,
        IRecordCountryFetcher fetcher,
        IRecordDiffService diff,
        IRecordRunArchive? archive = null,
        TimeSpan? pause = null,
        IRecordCountryRunProgress? progress = null)
    {
        _countries = countries;
        _fetcher = fetcher;
        _diff = diff;
        _archive = archive;
        _pause = pause ?? DefaultPause;
        _progress = progress;
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

        // Сохранённое прошлым прогоном (И-68) — только у прогона по ВСЕМ странам: подмножество
        // — отладка, и пусть оно честно ходит в источник. Ничего нет или устарело — копим заново.
        var saved = await LoadSavedAsync(status, codes, ct);

        // Мировые — один раз на прогон. Не скачались: прогон продолжается, а сторож
        // правдоподобия возьмёт эталон из базы (RecordDiffService.WorldReferenceAsync).
        if (saved?.World is { } savedWorld)
        {
            rows.AddRange(savedWorld);
            status.WorldFetched = true;
            status.RowsFetched = rows.Count;
        }
        else
        {
            try
            {
                var world = await _fetcher.FetchWorldAsync(ct);
                rows.AddRange(world);
                status.WorldFetched = true;
                status.RowsFetched = rows.Count;
                if (IsFullRun(codes)) await _progress!.SaveWorldAsync(world, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed.Add(new RecordCountryRunFailure(RecordCountryRun.WorldCode, ex.Message));
                status.Failed = failed.ToArray();
            }
        }

        var first = true;
        foreach (var country in selected)
        {
            ct.ThrowIfCancellationRequested();

            // Страна уже скачана оборванным прогоном — берём из сохранённого, источник не трогаем.
            if (saved != null && saved.Countries.TryGetValue(country.Code, out var kept))
            {
                Take(kept, rows, mismatches, status);
                status.Resumed++;
                status.Done++;
                status.RowsFetched = rows.Count;
                continue;
            }

            // Пауза — только между походами в источник: сохранённые страны его не нагружают.
            if (!first) await Task.Delay(_pause, ct);
            first = false;

            status.CurrentCode = country.Code;
            try
            {
                var fetched = await _fetcher.FetchAsync(country, ct);
                Take(fetched, rows, mismatches, status);
                if (IsFullRun(codes)) await _progress!.SaveCountryAsync(fetched, ct);
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

        // Архив ДО диффа и до Apply: файл фиксирует, что источник отдал сегодня, независимо
        // от того, применят прогон или посмотрят и закроют. Иначе «что стояло в прошлый раз»
        // снова остаётся без ответа — ровно как в И-21 и И-22.
        if (_archive != null)
        {
            var archived = await _archive.SaveCountryRunAsync(
                _fetcher.Source, codes, RecordCsvDump.Build(rows), ct);
            status.ArchivePath = archived.Path;
            status.ArchiveError = archived.Error;
        }

        status.Diff = await _diff.BuildDiffAsync(_fetcher.Source, rows, DiffTtl, ct);

        // Прошли все — сохранённое больше не нужно: следующий прогон обязан сходить в источник
        // заново, иначе «проверить ещё раз» молча вернул бы те же ответы. Упал кто-то (страна или
        // мировые) — держим: следующий прогон докачает только упавших.
        if (IsFullRun(codes) && failed.Count == 0)
            await _progress!.ClearAsync(ct);
    }

    /// <summary>Прогон по всем странам, и есть куда сохранять скачанное.</summary>
    private bool IsFullRun(IReadOnlyList<string>? codes) =>
        _progress != null && codes is not { Count: > 0 };

    private async Task<RecordCountryRunSaved?> LoadSavedAsync(
        RecordCountryRunStatus status, IReadOnlyList<string>? codes, CancellationToken ct)
    {
        if (!IsFullRun(codes)) return null;

        var saved = await _progress!.LoadAsync(_fetcher.Source, ct);
        if (saved is null)
        {
            await _progress.BeginAsync(_fetcher.Source, ct);
            return null;
        }

        status.ResumedFrom = saved.StartedAt;
        return saved;
    }

    /// <summary>Страна — в строки прогона и в счёт непринятых.</summary>
    private static void Take(
        RecordCountryFetchResult fetched, List<ParsedRecordDto> rows,
        List<RecordCountryMismatchDto> mismatches, RecordCountryRunStatus status)
    {
        rows.AddRange(fetched.Records);

        status.MismatchCount += fetched.Mismatches.Count;
        foreach (var m in fetched.Mismatches)
        {
            if (mismatches.Count >= MismatchSample) break;
            mismatches.Add(m);
        }
        status.Mismatches = mismatches.ToArray();
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
