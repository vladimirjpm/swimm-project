using Microsoft.Extensions.Logging;
using Swimm.Application.Abstractions;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Архив источников на диске — папка <c>!records-sources/</c> в корне репозитория
/// (её README и есть договор о формате и именах).
///
/// Почему прогон пишет выгрузку сам, а не человек командой после. Ручной
/// <c>--records-dump</c> под прогон по странам не годится: он сходил бы в источник ЕЩЁ РАЗ,
/// за теми же 430 файлами и ещё на час-два, и получил бы уже не то, что применили, а то,
/// что источник отдаёт сейчас. Прогон же держит разобранные строки в руках — выгрузка ему
/// ничего не стоит.
///
/// <b>Архивируются только боевые прогоны — по ВСЕМ странам.</b> Подмножество кодов в админке
/// подписано «повтор упавших или одна страна для отладки»: складывать в архив по файлу на
/// каждую отладочную перетяжку значит засыпать мусором ровно тот инструмент, ради которого
/// он заведён.
/// </summary>
public sealed class FileRecordRunArchive : IRecordRunArchive
{
    /// <summary>Папка архива относительно корня репозитория.</summary>
    public const string DefaultDirectoryName = "!records-sources";

    private readonly string _directory;
    private readonly ILogger<FileRecordRunArchive> _logger;
    private readonly Func<DateTime> _now;

    /// <param name="directory">Куда писать. Пусто — архив выключен, прогон просто ничего не пишет.</param>
    /// <param name="now">Часы; в тестах — фиксированные, иначе имя файла зависело бы от дня прогона теста.</param>
    public FileRecordRunArchive(
        string? directory, ILogger<FileRecordRunArchive> logger, Func<DateTime>? now = null)
    {
        _directory = directory ?? "";
        _logger = logger;
        _now = now ?? (() => DateTime.UtcNow);
    }

    public async Task<RecordArchiveResult> SaveCountryRunAsync(
        string source, IReadOnlyList<string>? scope, string csv, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_directory)) return RecordArchiveResult.Skipped;

        // Отладочный прогон подмножеством — мимо архива (см. комментарий класса).
        if (scope is { Count: > 0 }) return RecordArchiveResult.Skipped;

        try
        {
            Directory.CreateDirectory(_directory);

            var path = Path.Combine(_directory, FileName(source, _now()));

            // UTF-8 БЕЗ BOM: файл коммитится и сверяется контрольной суммой, BOM её сдвинет.
            // Перевод строки уже LF — его ставит RecordCsvDump, здесь пишем текст как есть.
            await File.WriteAllTextAsync(path, csv, new System.Text.UTF8Encoding(false), ct);

            _logger.LogInformation("Архив источников: выгрузка прогона сохранена в {Path}", path);
            return new RecordArchiveResult(path, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Прогон успешен, даже если архив не записался: час-два работы дороже файла.
            _logger.LogWarning(ex, "Архив источников: не удалось сохранить выгрузку прогона");
            return new RecordArchiveResult(null, ex.Message);
        }
    }

    /// <summary>
    /// Имя по договору README архива: <c>&lt;источник&gt;__fetched-&lt;дата&gt;.csv</c>.
    /// Суффикс <c>-countries</c> отличает выгрузку прогона по странам от ручной выгрузки того
    /// же источника: у <c>worldrecords</c> это разные наборы строк (мировые + 214 стран против
    /// мировых + Израиль), и складывать их под одним именем нельзя.
    /// </summary>
    public static string FileName(string source, DateTime fetchedAt) =>
        $"{source}-countries__fetched-{fetchedAt:yyyy-MM-dd}.csv";
}
