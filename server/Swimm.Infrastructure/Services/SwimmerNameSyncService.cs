using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// «Синхронизация языков» (Admin/Discovery): дозаполняет имена пловцов из двуязычной
/// пары PDF без переимпорта результатов. Матчинг и нормализация — те же, что в
/// <see cref="JsonImportService"/> (<see cref="SwimmerDedupService.Normalize"/>), чтобы
/// синхронизация находила ровно тех же пловцов, что нашёл бы импорт.
/// </summary>
public class SwimmerNameSyncService(SwimmDbContext db, ILogger<SwimmerNameSyncService> logger)
    : ISwimmerNameSyncService
{
    public async Task<SwimmerNameSyncResult> SyncFromResultsJsonAsync(string resultsJson, CancellationToken ct = default)
    {
        // Как JsonImportService: те же lenient-опции и оба формата —
        // ResultWrap { results: [...] } и простой массив Result[] (парсер отдаёт массив).
        var options = JsonImportService.CreateLenientOptions();
        List<ResultJsonItem> items;
        if (resultsJson.TrimStart().StartsWith('['))
            items = JsonSerializer.Deserialize<List<ResultJsonItem>>(resultsJson, options) ?? [];
        else
            items = JsonSerializer.Deserialize<ResultWrap>(resultsJson, options)?.Results ?? [];

        // Уникальные пловцы протокола (эстафеты пропускаем — там нет пары имён на участника).
        var swimmersInProtocol = items
            .Where(i => i.IsRelay != true && !string.IsNullOrWhiteSpace(i.LastName))
            .GroupBy(i => Key(i.LastName, i.FirstName, i.BirthYear))
            .Select(g => g.First())
            .ToList();

        var result = new SwimmerNameSyncResult { SwimmersInProtocol = swimmersInProtocol.Count };
        if (swimmersInProtocol.Count == 0) return result;

        // Реальных пловцов тысячи — грузим целиком (как импорт), два индекса:
        // по основным полям и по EN-полям.
        var all = await db.Swimmers
            .Where(s => s.SwimmerOrgId == null || !s.SwimmerOrgId.StartsWith("SYNTH-"))
            .ToListAsync(ct);
        var byMain = new Dictionary<string, Swimmer>();
        var byEn = new Dictionary<string, Swimmer>();
        foreach (var s in all)
        {
            byMain.TryAdd(Key(s.LastName, s.FirstName, s.BirthYear), s);
            if (!string.IsNullOrWhiteSpace(s.LastNameEn))
                byEn.TryAdd(Key(s.LastNameEn, s.FirstNameEn, s.BirthYear), s);
        }

        foreach (var item in swimmersInProtocol)
        {
            var heKey = Key(item.LastName, item.FirstName, item.BirthYear);
            // EN-имя «есть», только если оно реально отличается от HE (ParseBilingual
            // фоллбечит EN←HE при пустой английской строке).
            var hasEn = !string.IsNullOrWhiteSpace(item.LastNameEn)
                        && Key(item.LastNameEn, item.FirstNameEn, item.BirthYear) != heKey;
            var enKey = hasEn ? Key(item.LastNameEn, item.FirstNameEn, item.BirthYear) : null;

            if (byMain.TryGetValue(heKey, out var swimmer))
            {
                // Найден по ивритскому имени — дозаполняем только пустые EN-поля.
                if (hasEn && string.IsNullOrWhiteSpace(swimmer.LastNameEn))
                {
                    swimmer.LastNameEn = item.LastNameEn!;
                    swimmer.FirstNameEn = item.FirstNameEn ?? string.Empty;
                    byEn.TryAdd(enKey!, swimmer);
                    result.EnNamesFilled++;
                }
                else result.AlreadyComplete++;
                continue;
            }

            if (enKey != null && byEn.TryGetValue(enKey, out swimmer))
            {
                // EN-поля уже заполнены (пловец известен под этим EN-именем);
                // основные поля не трогаем — HE-написание могло отличаться вариантом.
                result.AlreadyComplete++;
                continue;
            }

            if (enKey != null && byMain.TryGetValue(enKey, out swimmer))
            {
                // Создан из EN-протокола (английское имя в основных полях) — канонизируем:
                // HE в основные поля, EN в *En (решение Влада 2026-07-18).
                logger.LogInformation(
                    "Канонизация пловца {Id}: '{OldLast} {OldFirst}' → HE '{HeLast} {HeFirst}' + EN '{EnLast} {EnFirst}'",
                    swimmer.Id, swimmer.LastName, swimmer.FirstName,
                    item.LastName, item.FirstName, item.LastNameEn, item.FirstNameEn);
                swimmer.LastName = item.LastName ?? string.Empty;
                swimmer.FirstName = item.FirstName ?? string.Empty;
                swimmer.LastNameEn = item.LastNameEn!;
                swimmer.FirstNameEn = item.FirstNameEn ?? string.Empty;
                byMain.TryAdd(heKey, swimmer);
                byEn.TryAdd(enKey, swimmer);
                result.Canonized++;
                continue;
            }

            result.NotFound++;
        }

        await db.SaveChangesAsync(ct);
        return result;
    }

    private static string Key(string? last, string? first, int? year) =>
        $"{SwimmerDedupService.Normalize(last ?? "")}|{SwimmerDedupService.Normalize(first ?? "")}|{year ?? 0}";
}
