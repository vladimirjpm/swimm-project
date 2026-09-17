namespace Swimm.Application.Abstractions;

/// <summary>
/// Чем кончилась попытка сохранить выгрузку.
/// </summary>
/// <param name="Path">Куда легло; null — не сохраняли (архив выключен или прогон отладочный).</param>
/// <param name="Error">Почему не сохранилось; null — беды не было.</param>
public sealed record RecordArchiveResult(string? Path, string? Error)
{
    public static readonly RecordArchiveResult Skipped = new(null, null);
}

/// <summary>
/// Архив источников справочника рекордов: куда прогон складывает выгрузку разобранных строк
/// (`!records-sources/README.md`).
///
/// Порт, а не прямой <c>File.WriteAllText</c> в прогоне: писать на диск — I/O, а прогон
/// (<c>RecordCountryRunner</c>) обязан прогоняться тестом без диска и без хоста.
///
/// Зачем архив вообще: PDF доказывает, ЧТО отдал источник, а CSV отвечает на вопрос «что
/// стояло в этой строке в прошлый раз» — тот самый, на который 16.09.2026 (И-21) ответить
/// было нечем, а 17.09.2026 (И-22) пришлось отвечать косвенно, счётом по всем странам.
/// </summary>
public interface IRecordRunArchive
{
    /// <summary>
    /// Сохранить выгрузку прогона.
    ///
    /// ⚠ <b>Не бросает.</b> Прогон по 215 странам идёт час-два, и упавшая запись файла не
    /// повод терять его результат — беда возвращается в <see cref="RecordArchiveResult.Error"/>
    /// и доезжает до админа отдельным полем статуса.
    /// </summary>
    /// <param name="source">Ключ источника (<c>worldrecords</c>) — в имя файла.</param>
    /// <param name="scope">
    /// Охват прогона: <c>null</c> — все страны (боевой прогон), иначе список кодов
    /// (отладочный прогон подмножеством, он не архивируется).
    /// </param>
    /// <param name="csv">Готовое содержимое файла (<c>RecordCsvDump.Build</c>).</param>
    Task<RecordArchiveResult> SaveCountryRunAsync(
        string source, IReadOnlyList<string>? scope, string csv, CancellationToken ct = default);
}
