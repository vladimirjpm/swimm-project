namespace Swimm.Application.Abstractions;

/// <summary>
/// Р125: пол пловца — с карточки loglig, когда у нас он пуст. Решает
/// <see cref="Swimm.Application.Mapping.LogligCardGenderRule"/> (пол пуст, loglig-id подтверждён,
/// имя и год карточки совпали, пол на карточке однозначен); здесь — кого спрашивать, сеть
/// (последовательно, с паузой и потолком запросов), суточный ответ карточки из общего места
/// <c>Sys_SwimmerLogligClubs</c> и запись.
///
/// Запись — тем же действием, что кнопки ♂/♀ в Health (<see cref="IDataCheckRunner.SetSwimmerGenderAsync"/>):
/// пол пловцу и его личным строкам БЕЗ пола. Непустой пол не перезаписывается нигде. На каждого
/// пловца — строка аудита <c>swimmer.gender-from-loglig</c> (источник «loglig card», id игрока).
/// </summary>
public interface ILogligCardGenderService
{
    /// <summary>Кнопка в Health: пловцы открытых находок <c>results.no-gender</c>.</summary>
    Task<LogligCardGenderReport> FillFromFindingsAsync(CancellationToken ct = default);

    /// <summary>После импорта: пловцы соревнования с этим compID isr.org.il.</summary>
    Task<LogligCardGenderReport> FillAfterImportAsync(int orgCompId, CancellationToken ct = default);

    /// <summary>После импорта дня-соседа (Р124): пловцы одного дня.</summary>
    Task<LogligCardGenderReport> FillDayAfterImportAsync(int competitionId, CancellationToken ct = default);
}

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
/// <param name="Notes">Строки про несовпавших и неоткрывшихся — кого смотреть руками.</param>
public sealed record LogligCardGenderReport(
    int Candidates, int Set, int Rows, int CardWithoutGender, int Failed, int Mismatch,
    int NotVerified, int OverLimit, int Requests, int FromCache, int AlreadyKnown,
    string Message, IReadOnlyList<string> Notes);
