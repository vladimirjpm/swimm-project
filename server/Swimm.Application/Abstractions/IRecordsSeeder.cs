namespace Swimm.Application.Abstractions;

/// <summary>
/// Одноразовый перенос рекордов/нормативов из легаси JS-файлов клиента
/// (client/public/data/normative*.js) в таблицы Records / NormativeStandards.
/// Запускается явно: dotnet run -- --seed-records &lt;путь-к-data-dir&gt; [--force].
/// </summary>
public interface IRecordsSeeder
{
    /// <summary>
    /// Прочитать 5 normative*.js из <paramref name="dataDirectory"/> и залить в БД.
    /// Непустые таблицы — отказ без <paramref name="force"/> (защита правок админа);
    /// с force содержимое обеих таблиц заменяется целиком.
    /// Возвращает лог со счётчиками "распарсено/вставлено" по каждому файлу.
    /// </summary>
    Task<IReadOnlyList<string>> SeedAsync(string dataDirectory, bool force = false);
}
