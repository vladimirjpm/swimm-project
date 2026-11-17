using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Что прошлый (оборванный или неполный) прогон по странам успел скачать.
/// </summary>
/// <param name="StartedAt">Когда начат прогон, с которого копится это сохранённое.</param>
/// <param name="World">Мировые рекорды; null — их тогда не скачали, качать заново.</param>
/// <param name="Countries">Скачанные страны по коду. Упавших здесь нет — их качают заново.</param>
public sealed record RecordCountryRunSaved(
    DateTimeOffset StartedAt,
    IReadOnlyList<ParsedRecordDto>? World,
    IReadOnlyDictionary<string, RecordCountryFetchResult> Countries);

/// <summary>
/// Скачанное прогоном по странам — на диске, по стране сразу после скачивания. Следующий
/// прогон по всем странам берёт уже скачанное отсюда и ходит в источник только за остальным.
///
/// Зачем: прогон идёт полтора часа и живёт в памяти процесса. 16.11.2026 ProdAdmin закрылся на
/// стране 103 из 214, и всё скачанное пропало вместе с процессом — начинать пришлось с нуля
/// (И-68). Сохраняется только ответ источника, не состояние базы: дифф по-прежнему строится в
/// конце прогона от текущей базы, поэтому сохранённое годится и после Apply, и после рестарта.
///
/// Порт, а не файлы прямо в прогоне, — как <see cref="IRecordRunArchive"/>: прогон обязан
/// прогоняться тестом без диска.
///
/// ⚠ <b>Ни один метод не бросает</b> (кроме отмены). Сохранённое — подстраховка: не записалось —
/// прогон идёт как раньше, просто при обрыве начнётся заново.
/// </summary>
public interface IRecordCountryRunProgress
{
    /// <summary>
    /// Сохранённое прошлым прогоном того же источника — или null: ничего нет, оно устарело
    /// (рекорды меняются, сутки-старые ответы склеивать со свежими нельзя) или файл испорчен.
    /// </summary>
    Task<RecordCountryRunSaved?> LoadAsync(string source, CancellationToken ct = default);

    /// <summary>Начать копить заново: стереть прошлое и запомнить время начала.</summary>
    Task BeginAsync(string source, CancellationToken ct = default);

    Task SaveWorldAsync(IReadOnlyList<ParsedRecordDto> rows, CancellationToken ct = default);

    Task SaveCountryAsync(RecordCountryFetchResult result, CancellationToken ct = default);

    /// <summary>Прогон прошёл целиком — сохранённое больше не нужно, следующий начнёт с нуля.</summary>
    Task ClearAsync(CancellationToken ct = default);
}
