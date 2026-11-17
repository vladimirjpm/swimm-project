using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Скачанное прогоном по странам — в папке на диске (И-68): <c>run.json</c> (источник и время
/// начала), <c>world.json</c>, по файлу <c>country-&lt;код&gt;.json</c> на страну. Файл страны
/// пишется сразу после её скачивания, через временный файл и переименование: оборвись процесс
/// посреди записи — останется либо старый файл, либо никакого, но не половина.
///
/// Папка по умолчанию — во временной папке системы, вне репозитория: это не архив (архив —
/// <see cref="FileRecordRunArchive"/>), а черновик одного прогона, и в git ему делать нечего.
/// В ней только ответ источника, никаких данных базы: локальный API и ProdAdmin могут
/// продолжить прогон друг друга — источник для них один.
/// </summary>
public sealed partial class FileRecordCountryRunProgress : IRecordCountryRunProgress
{
    /// <summary>
    /// Сколько сохранённое годится. Прогон идёт полтора часа; сутки — запас на «закрыл вечером,
    /// продолжил утром». Старше — склеивать с ним свежие ответы нельзя: часть стран была бы
    /// сверена позавчера, а журнал подписал бы весь прогон сегодняшним днём.
    /// </summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromHours(24);

    private const string RunFile = "run.json";
    private const string WorldFile = "world.json";
    private const string CountryPrefix = "country-";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    private readonly string _directory;
    private readonly ILogger<FileRecordCountryRunProgress> _logger;
    private readonly Func<DateTimeOffset> _now;

    /// <param name="directory">Папка; null или пусто — папка по умолчанию во временной папке системы.</param>
    /// <param name="now">Часы; в тестах — подставные, чтобы проверить срок годности.</param>
    public FileRecordCountryRunProgress(
        string? directory, ILogger<FileRecordCountryRunProgress> logger, Func<DateTimeOffset>? now = null)
    {
        _directory = string.IsNullOrWhiteSpace(directory) ? DefaultDirectory() : directory;
        _logger = logger;
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    public static string DefaultDirectory() =>
        Path.Combine(Path.GetTempPath(), "swimm", "record-country-run");

    private sealed record RunInfo(string Source, DateTimeOffset StartedAt);

    public async Task<RecordCountryRunSaved?> LoadAsync(string source, CancellationToken ct = default)
    {
        try
        {
            var runPath = Path.Combine(_directory, RunFile);
            if (!File.Exists(runPath)) return null;

            var run = await ReadAsync<RunInfo>(runPath, ct);
            if (run is null || !string.Equals(run.Source, source, StringComparison.OrdinalIgnoreCase))
                return null;

            var age = _now() - run.StartedAt;
            if (age > MaxAge || age < TimeSpan.Zero)
            {
                _logger.LogInformation(
                    "Прогон по странам: сохранённое от {StartedAt} старше {MaxAge} — начинаем заново",
                    run.StartedAt, MaxAge);
                return null;
            }

            var worldPath = Path.Combine(_directory, WorldFile);
            var world = File.Exists(worldPath) ? await ReadOrNullAsync<List<ParsedRecordDto>>(worldPath, ct) : null;

            var countries = new Dictionary<string, RecordCountryFetchResult>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in Directory.EnumerateFiles(_directory, CountryPrefix + "*.json"))
            {
                // Испорченный файл одной страны — не повод выбрасывать остальные: эту просто
                // скачаем заново.
                var result = await ReadOrNullAsync<RecordCountryFetchResult>(path, ct);
                if (result is { Code.Length: > 0, Records: not null, Mismatches: not null })
                    countries[result.Code] = result;
            }

            return new RecordCountryRunSaved(run.StartedAt, world, countries);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Прогон по странам: сохранённое не прочиталось — начинаем заново");
            return null;
        }
    }

    public async Task BeginAsync(string source, CancellationToken ct = default)
    {
        await SafeAsync("начать сохранение", async () =>
        {
            DeleteAll();
            Directory.CreateDirectory(_directory);
            await WriteAsync(RunFile, new RunInfo(source, _now()), ct);
        });
    }

    public Task SaveWorldAsync(IReadOnlyList<ParsedRecordDto> rows, CancellationToken ct = default) =>
        SafeAsync("сохранить мировые", () => WriteAsync(WorldFile, rows, ct));

    public Task SaveCountryAsync(RecordCountryFetchResult result, CancellationToken ct = default)
    {
        // Код идёт в имя файла — только буквы и цифры, чтобы никакой код источника не вывел
        // запись за пределы папки.
        if (!SafeCode().IsMatch(result.Code))
        {
            _logger.LogWarning("Прогон по странам: код {Code} не годится для имени файла — не сохраняем", result.Code);
            return Task.CompletedTask;
        }

        return SafeAsync($"сохранить {result.Code}",
            () => WriteAsync($"{CountryPrefix}{result.Code.ToUpperInvariant()}.json", result, ct));
    }

    public Task ClearAsync(CancellationToken ct = default) =>
        SafeAsync("очистить сохранённое", () => { DeleteAll(); return Task.CompletedTask; });

    private void DeleteAll()
    {
        if (!Directory.Exists(_directory)) return;
        foreach (var path in Directory.EnumerateFiles(_directory, "*.json"))
            File.Delete(path);
        foreach (var path in Directory.EnumerateFiles(_directory, "*.tmp"))
            File.Delete(path);
    }

    private async Task WriteAsync<T>(string name, T value, CancellationToken ct)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, name);
        var tmp = path + ".tmp";

        await using (var stream = File.Create(tmp))
            await JsonSerializer.SerializeAsync(stream, value, Json, ct);

        File.Move(tmp, path, overwrite: true);
    }

    private static async Task<T?> ReadAsync<T>(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, Json, ct);
    }

    private async Task<T?> ReadOrNullAsync<T>(string path, CancellationToken ct) where T : class
    {
        try
        {
            return await ReadAsync<T>(path, ct);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            _logger.LogWarning(ex, "Прогон по странам: {Path} не прочитался — скачаем заново", path);
            return null;
        }
    }

    private async Task SafeAsync(string what, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Сохранённое — подстраховка: не записалось — прогон идёт дальше как раньше.
            _logger.LogWarning(ex, "Прогон по странам: не удалось {What}", what);
        }
    }

    [GeneratedRegex("^[A-Za-z0-9]{2,8}$")]
    private static partial Regex SafeCode();
}
