using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Проставить пловцам соревнования их loglig-id, взятые ИЗ САМОГО ПРОТОКОЛА: на странице
/// заплыва loglig имя напечатано ссылкой на карточку (docs/admin-pages/competitions.md).
///
/// Зачем: привязка вручную идёт по одному пловцу, и на 5.5 тысяч человек их набралось меньше
/// сотни — а без привязки не работает ни сверка подозрительных рекордов, ни карточка. Импорт
/// же и так знает, кто плыл этот старт, и знает соревнование на loglig.
///
/// Что НЕ делает: не трогает уже привязанных (связь в базе — решение человека), не гадает при
/// тёзках и не отбирает id у другого пловца.
/// </summary>
public interface ILogligStampService
{
    /// <summary>
    /// Пройти пловцов соревнования (по <paramref name="orgCompId"/> — compID isr.org.il) и
    /// привязать тех, кого удалось однозначно опознать в протоколе loglig.
    /// </summary>
    Task<LogligStampReport> StampFromProtocolAsync(int orgCompId, CancellationToken ct = default);

    /// <summary>
    /// Разовый проход по ВСЕМ импортированным соревнованиям, у которых есть loglig-id
    /// (CLI: <c>--stamp-loglig-ids</c>). Соревнования без единого непривязанного пловца
    /// пропускаются, не заглядывая на сайт: смысл прохода — добрать долги, а не перечитать
    /// весь архив.
    /// </summary>
    Task<LogligStampBackfillReport> BackfillAsync(CancellationToken ct = default);
}
