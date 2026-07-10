namespace Swimm.Application.Abstractions;

/// <summary>
/// Одноразовый перенос ТРЕНИРОВОК «Дельфин-мастерс» из dolphin_masters_data.json в
/// Sys_TrainingSessions / Sys_TrainingResults (соревнования не трогаем — они уже в БД).
/// Идентичность пловцов берётся из вычитанного вручную словаря canon-resolved.csv
/// (см. docs/tasks/hubgroups-dolphin-import.md). Local-пловцы создаются с Origin='local'.
/// Запуск: dotnet run -- --seed-dolphin-training &lt;json&gt; &lt;csv&gt; --group &lt;hubGroupId&gt; [--force].
/// </summary>
public interface IDolphinTrainingSeeder
{
    /// <summary>
    /// Прочитать JSON + словарь, создать недостающих local-пловцов и залить тренировки в группу
    /// <paramref name="hubGroupId"/>. Идемпотентно по натуральному ключу
    /// (ExternalTrainingId, SwimmerId, StyleId, Distance, SetNo, OrderNo): повтор не задваивает.
    /// <paramref name="force"/> — сначала удалить ранее засиженные тренировки этой группы.
    /// Возвращает лог со счётчиками.
    /// </summary>
    Task<IReadOnlyList<string>> SeedAsync(string jsonPath, string canonCsvPath, int hubGroupId, bool force = false);
}
