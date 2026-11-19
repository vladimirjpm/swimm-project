using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Очередь «Проверить в loglig» (Р112, /Admin/Clubs): одна пара или все пары «Общие пловцы».
/// Сотня карточек — это минуты (~1 запрос в секунду), в HTTP-запрос админки не пролезает, поэтому
/// фоновая задача; страница опрашивает <see cref="GetStatus"/>. Устройство — как у прогона рекордов
/// по странам (<see cref="IRecordCountryRunQueue"/>): статусы в памяти процесса.
///
/// Активный прогон один: loglig — сайт федерации, два параллельных обхода ему ни к чему.
/// </summary>
public interface IClubPairLogligCheckQueue
{
    /// <summary>
    /// Поставить прогон. <paramref name="clubA"/>/<paramref name="clubB"/> null — все пары.
    /// Если уже идёт (или ждёт) другой прогон, новый не ставится: возвращается текущий и false.
    /// </summary>
    (ClubPairLogligRunStatus Status, bool Started) Enqueue(int? clubA, int? clubB);

    ClubPairLogligRunStatus? GetStatus(Guid runId);

    /// <summary>Активный прогон, иначе последний; null — с запуска процесса не было ни одного.</summary>
    ClubPairLogligRunStatus? Current();

    /// <summary>Все прогоны с запуска процесса (/Admin/Jobs).</summary>
    IReadOnlyList<ClubPairLogligRunStatus> Snapshot();
}
