using Swimm.Application.Mapping;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Р125: пол пловца — с карточки loglig, когда у нас он пуст. Решает
/// <see cref="LogligCardGenderRule"/> (пол пуст, loglig-id подтверждён, имя и год карточки
/// совпали, пол на карточке однозначен); здесь — кого спрашивать, сеть (последовательно, с паузой
/// и потолком запросов), суточный ответ карточки из общего места <c>Sys_SwimmerLogligClubs</c> и
/// запись.
///
/// Р126: автоматически пол пишется, только если личность подтверждена заплывами и серьёзных
/// последствий нет (<see cref="GenderImpactRules"/>); иначе случай ждёт человека — находка
/// <c>swimmers.gender-from-loglig-review</c> и <see cref="ConfirmAsync"/>.
///
/// Запись — тем же действием, что кнопки ♂/♀ в Health, но условная
/// (<see cref="IDataCheckRunner.SetSwimmerGenderIfEmptyAsync"/>): пол пловцу и его личным строкам
/// БЕЗ пола. На каждого пловца — строка аудита (источник «loglig card», id игрока).
/// </summary>
public interface ILogligCardGenderService
{
    /// <summary>
    /// Кнопка в Health: пловцы открытых находок <c>results.no-gender</c>. null — прогон уже идёт
    /// (второе нажатие или шаг импорта): одновременно по карточкам ходит один.
    /// Отмена <paramref name="ct"/> (потолок времени кнопки) не бросает: возвращается честный
    /// частичный итог с <see cref="LogligCardGenderReport.Interrupted"/>.
    /// </summary>
    Task<LogligCardGenderReport?> FillFromFindingsAsync(CancellationToken ct = default);

    /// <summary>После импорта: пловцы соревнования с этим compID isr.org.il.</summary>
    Task<LogligCardGenderReport> FillAfterImportAsync(int orgCompId, CancellationToken ct = default);

    /// <summary>После импорта дня-соседа (Р124): пловцы одного дня.</summary>
    Task<LogligCardGenderReport> FillDayAfterImportAsync(int competitionId, CancellationToken ct = default);

    /// <summary>
    /// «Проставить» в находке ручной проверки: человек видел последствия и согласен. Пол — с
    /// сохранённой карточки (не от клиента); после записи пересчитываются производные величины
    /// затронутых соревнований. null — нечего проставлять (пол уже стоит, карточки нет, loglig-id
    /// сменился).
    /// </summary>
    Task<LogligGenderConfirmResult?> ConfirmAsync(int swimmerId, CancellationToken ct = default);

    /// <summary>То же по находке `swimmers.gender-from-loglig-review`; null — находка не та или проставлять нечего.</summary>
    Task<LogligGenderConfirmResult?> ConfirmFindingAsync(int findingId, CancellationToken ct = default);
}

/// <summary>Случай, ждущий человека: что говорит loglig и что изменится, если проставить.</summary>
public sealed record LogligGenderReview(
    int SwimmerId, string SwimmerName, int BirthYear, int LogligId, string Gender, string? CardName,
    IReadOnlyList<GenderImpactItem> Reasons);

/// <summary>Итог «Проставить»: пол, сколько строк дописано, сколько соревнований пересчитано.</summary>
public sealed record LogligGenderConfirmResult(string Gender, int Rows, int Recalculated);

/// <summary>Итог прогона «пол с карточек loglig».</summary>
/// <param name="Candidates">Пловцов без пола в выборке.</param>
/// <param name="Set">Скольким пол проставлен.</param>
/// <param name="Rows">Сколько строк результата получили пол.</param>
/// <param name="CardWithoutGender">Карточка открылась, пола на ней нет.</param>
/// <param name="Failed">Карточка не открылась.</param>
/// <param name="Mismatch">Имя или год карточки не совпали с пловцом — не пишем.</param>
/// <param name="NotVerified">Нет подтверждённого loglig-id — спросить не у кого.</param>
/// <param name="OverLimit">Не дошли: потолок запросов исчерпан.</param>
/// <param name="Requests">Запросов к loglig за прогон.</param>
/// <param name="FromCache">Ответов взято из суточного общего места, без сети.</param>
/// <param name="AlreadyKnown">Пловцов выборки, у кого пол уже известен (их строки дописывает соседняя кнопка).</param>
/// <param name="Review">Р126: не проставлено автоматически, ждут ручной проверки.</param>
/// <param name="DbFailed">Сбой записи на пловце — остальные не пострадали.</param>
/// <param name="Interrupted">Прогон прерван по времени; обработано <paramref name="Reached"/> из <paramref name="Candidates"/>.</param>
/// <param name="Notes">Строки про несовпавших, неоткрывшихся и ждущих проверки — кого смотреть руками.</param>
public sealed record LogligCardGenderReport(
    int Candidates, int Set, int Rows, int CardWithoutGender, int Failed, int Mismatch,
    int NotVerified, int OverLimit, int Requests, int FromCache, int AlreadyKnown,
    string Message, IReadOnlyList<string> Notes,
    int Review = 0, int DbFailed = 0, bool Interrupted = false, int Reached = 0);
