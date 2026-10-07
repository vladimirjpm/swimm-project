namespace Swimm.Parsing.Discovery;

/// <summary>
/// loglig ответил на запрос PDF промежуточных дисциплины не PDF-файлом — 200 с пустым телом
/// при живых результатах (6595, loglig 11586: мужская 4X50 комплекс 58140, 07.10.2026).
///
/// Это не сбой сети и не ошибка разбора: у источника для этой дисциплины промежуточных НЕТ,
/// и повтор их не даст. Поэтому отдельный тип — доклейка пропускает такую дисциплину и
/// называет её в отчёте, а не роняет всё соревнование. Наследник
/// <see cref="InvalidOperationException"/>: прежние обработчики ловят его как раньше.
/// </summary>
public sealed class LogligSplitPdfMissingException(int logligId, int disciplineId)
    : InvalidOperationException(
        $"loglig вернул не PDF промежуточных для дисциплины {disciplineId} (соревнование {logligId}).")
{
    public int DisciplineId { get; } = disciplineId;
}
