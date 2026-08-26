using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Проверка подозрительных заплывов превью по карточке пловца на loglig: то ли это время,
/// что напечатано в протоколе (docs/admin-pages/competitions.md, «Рекорды в превью»).
///
/// Зачем: «файл побьёт 14 рекордов» чаще значит кривой разбор, чем 14 рекордов. Самая
/// быстрая проверка — карточка пловца на loglig: протокол мы качаем оттуда же, значит его
/// время там должно быть. Совпало — рекорд настоящий; на loglig время хуже — разбор поехал.
///
/// Работает ЛЕНИВО, отдельным запросом после показа превью: это по запросу на пловца, и
/// тормозить ими сам разбор (тем более пакетный забор) нельзя.
/// </summary>
public interface IPreviewRecordCheckService
{
    /// <summary>
    /// Строки проверки по отложенному разбору. Пустой список — превью истекло либо в нём
    /// нет побитых рекордов.
    /// </summary>
    Task<IReadOnlyList<PreviewRecordCheckRow>> CheckAsync(Guid previewId, CancellationToken ct = default);
}
