using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Constants;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Журнал проверок источников рекордов (docs/plans/records-freshness-plan.md, U2–U3).
///
/// Проверка = fetch провайдера → хэш разобранных строк → dry-run дифф
/// (<see cref="IRecordDiffService.BuildDiffAsync"/>) → строка <c>Sys_RecordSourceChecks</c>.
/// В базу рекордов проверка не пишет НИЧЕГО: Apply — отдельный шаг человека, потому что
/// источник ошибается (И-21, И-26), и дифф должен посмотреть глаз.
///
/// Хэш не экономит разбор (провайдеры отдают уже разобранные строки), он отвечает на другой
/// вопрос: «менял ли источник данные» против «менялась ли наша база». Дифф пустой при новом
/// хэше — источник пересобрал файл, данные те же.
/// </summary>
public class RecordSourceCheckService : IRecordSourceCheckService
{
    private const int ErrorMaxLength = 2000;

    private readonly SwimmDbContext _db;
    private readonly IReadOnlyDictionary<string, IRecordSourceProvider> _providers;
    private readonly IRecordDiffService _diff;
    private readonly Func<DateTime> _now;

    public RecordSourceCheckService(
        SwimmDbContext db, IEnumerable<IRecordSourceProvider> providers, IRecordDiffService diff)
        : this(db, providers, diff, () => DateTime.UtcNow)
    {
    }

    /// <summary>С подменой часов — для тестов «старше 7 дней».</summary>
    public RecordSourceCheckService(
        SwimmDbContext db, IEnumerable<IRecordSourceProvider> providers, IRecordDiffService diff,
        Func<DateTime> now)
    {
        _db = db;
        _providers = providers.ToDictionary(p => p.Source, StringComparer.OrdinalIgnoreCase);
        _diff = diff;
        _now = now;
    }

    public async Task<RecordSourceCheckResultDto> CheckAsync(
        string source, RecordSourceRequest? request = null, CancellationToken ct = default)
    {
        if (!_providers.TryGetValue(source, out var provider))
            throw new ArgumentException(
                $"Неизвестный источник '{source}'. Доступны: {string.Join(", ", _providers.Keys)}", nameof(source));

        var check = new RecordSourceCheck { Source = provider.Source, CheckedAt = _now() };
        RecordDiffResult? diff = null;

        try
        {
            var parsed = await provider.FetchAsync(
                request ?? new RecordSourceRequest(provider.Source), ct);

            if (parsed.Count == 0)
                throw new InvalidOperationException(
                    "Источник разобран, но не дал ни одной строки — проверьте файл/URL.");

            check.ContentHash = ContentHash(parsed);
            diff = await _diff.BuildDiffAsync(provider.Source, parsed, ct: ct);

            check.DiffId = diff.DiffId;
            // Клетку с двумя хозяевами (country/*/open: WA и федерация, И-13) не считаем:
            // WA приносит туда своё устаревшее значение при КАЖДОЙ проверке, а следующий шаг
            // цепочки кладёт федеральное обратно. Замер 21.09.2026: 86 «изменений» worldrecords,
            // все до одного — Израиль open. Считай мы их — «ждёт Apply» не гасло бы никогда.
            check.AddedCount = diff.Added.Count(e => !RecordPlausibility.IsContestedSlot(e));
            check.ChangedCount = diff.Changed.Count(e => !RecordPlausibility.IsContestedSlot(e));
            check.MissingCount = diff.MissingInSourceCount;
            // «Нет в источнике» Apply не удаляет — это не изменение, которое ждёт человека.
            check.Outcome = check.AddedCount + check.ChangedCount > 0
                ? RecordSourceCheckOutcomes.ChangesFound
                : RecordSourceCheckOutcomes.Unchanged;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Любой сбой источника — строка failed, а не исключение наружу: при «проверить все»
            // упавший World Aquatics не должен оставить федерацию непроверенной. Отмена самим
            // вызывающим — не сбой источника, её пробрасываем.
            check.Outcome = RecordSourceCheckOutcomes.Failed;
            check.Error = Trim(ex.Message);
            diff = null;
        }

        _db.RecordSourceChecks.Add(check);
        await _db.SaveChangesAsync(ct);

        return new RecordSourceCheckResultDto(
            check.Source, check.Id, check.Outcome, check.CheckedAt, check.Error, diff);
    }

    public async Task LogRunAsync(
        string source, RecordDiffResult? diff, string? error, CancellationToken ct = default)
    {
        var check = new RecordSourceCheck { Source = source, CheckedAt = _now() };

        if (error != null)
        {
            check.Outcome = RecordSourceCheckOutcomes.Failed;
            check.Error = Trim(error);
        }
        else if (diff != null)
        {
            check.DiffId = diff.DiffId;
            // Счёт тот же, что у обычной проверки: клетка с двумя хозяевами не считается
            // изменением (И-13), «нет в источнике» Apply не удаляет.
            check.AddedCount = diff.Added.Count(e => !RecordPlausibility.IsContestedSlot(e));
            check.ChangedCount = diff.Changed.Count(e => !RecordPlausibility.IsContestedSlot(e));
            check.MissingCount = diff.MissingInSourceCount;
            check.Outcome = check.AddedCount + check.ChangedCount > 0
                ? RecordSourceCheckOutcomes.ChangesFound
                : RecordSourceCheckOutcomes.Unchanged;
        }

        _db.RecordSourceChecks.Add(check);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<RecordSourceCheckResultDto>> CheckAllAsync(CancellationToken ct = default)
    {
        var results = new List<RecordSourceCheckResultDto>();
        foreach (var source in RecordSources.Order)
        {
            if (!_providers.ContainsKey(source)) continue;
            results.Add(await CheckAsync(source, ct: ct));
        }
        return results;
    }

    public async Task<RecordDiffApplyResult> ApplyAsync(RecordDiffApplyRequest request, CancellationToken ct = default)
    {
        var check = string.IsNullOrEmpty(request.DiffId)
            ? null
            : await _db.RecordSourceChecks.FirstOrDefaultAsync(c => c.DiffId == request.DiffId, ct);

        if (check != null)
        {
            // Источник успели проверить ещё раз — этот дифф описывает ВЧЕРАШНИЙ источник. Даже
            // если он совпадает со свежим, журнал не должен врать, какая проверка применена.
            var superseded = await _db.RecordSourceChecks.AnyAsync(c =>
                c.Source == check.Source && c.CheckedAt > check.CheckedAt
                && c.Outcome != RecordSourceCheckOutcomes.Failed, ct);
            if (superseded)
                return new RecordDiffApplyResult(false,
                    "Источник уже перепроверен после этого диффа — примените дифф последней проверки.", 0);
        }

        var result = await _diff.ApplyAsync(request, ct);

        if (result.Success && check != null)
        {
            check.AppliedAt = _now();
            await _db.SaveChangesAsync(ct);
        }

        return result;
    }

    public async Task<IReadOnlyList<RecordSourceFreshnessDto>> GetFreshnessAsync(CancellationToken ct = default)
    {
        var now = _now();
        var changedAt = (await _diff.GetSourceStatusAsync(ct))
            .ToDictionary(s => s.Source, s => s.LastUpdatedAt, StringComparer.OrdinalIgnoreCase);

        var result = new List<RecordSourceFreshnessDto>();
        foreach (var source in RecordSources.FreshnessKeys)
        {
            var last = await _db.RecordSourceChecks.AsNoTracking()
                .Where(c => c.Source == source)
                .OrderByDescending(c => c.CheckedAt).ThenByDescending(c => c.Id)
                .FirstOrDefaultAsync(ct);
            var lastOk = last == null || last.Outcome != RecordSourceCheckOutcomes.Failed
                ? last
                : await _db.RecordSourceChecks.AsNoTracking()
                    .Where(c => c.Source == source && c.Outcome != RecordSourceCheckOutcomes.Failed)
                    .OrderByDescending(c => c.CheckedAt).ThenByDescending(c => c.Id)
                    .FirstOrDefaultAsync(ct);
            var appliedAt = await _db.RecordSourceChecks.AsNoTracking()
                .Where(c => c.Source == source && c.AppliedAt != null)
                .MaxAsync(c => c.AppliedAt, ct);

            // Ждёт Apply только последняя успешная проверка с изменениями: старые диффы
            // перекрыты ею (см. ApplyAsync), считать их — показывать то, что уже неправда.
            var pending = lastOk is { Outcome: RecordSourceCheckOutcomes.ChangesFound, AppliedAt: null };

            var failed = last?.Outcome == RecordSourceCheckOutcomes.Failed;
            var stale = lastOk == null || now - lastOk.CheckedAt > RecordSources.StaleAfter;

            result.Add(new RecordSourceFreshnessDto(
                source,
                lastOk?.CheckedAt,
                last?.CheckedAt,
                last?.Outcome,
                failed ? last!.Error : null,
                pending ? lastOk!.AddedCount ?? 0 : 0,
                pending ? lastOk!.ChangedCount ?? 0 : 0,
                appliedAt,
                changedAt.GetValueOrDefault(source),
                failed || stale));
        }
        return result;
    }

    /// <summary>
    /// SHA-256 разобранных строк в каноническом порядке. Порядок строк провайдера не
    /// гарантирован (четыре запроса параллельно у <c>wa-junior</c>), поэтому сортируем.
    /// </summary>
    public static string ContentHash(IEnumerable<ParsedRecordDto> rows)
    {
        var lines = rows
            .Select(r => string.Join('',
                r.RegionType, r.RegionCode, r.Category, r.AgeKey, r.Gender, r.PoolType, r.Style,
                r.Distance, r.Time, r.HolderName, r.Club, r.HolderCountry, r.RecordDate))
            .Order(StringComparer.Ordinal);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', lines)));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string Trim(string message) =>
        message.Length <= ErrorMaxLength ? message : message[..ErrorMaxLength];
}
