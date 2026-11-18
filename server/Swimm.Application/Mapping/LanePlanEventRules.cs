using Swimm.Application.Dtos;
using Swimm.Domain.Entities;

namespace Swimm.Application.Mapping;

/// <summary>
/// Правила ленты «Since publishing» опубликованного плана (docs/plans/trainings-coach-flow-plan.md,
/// CF8) — чистые функции, без БД. Место в плане — число: null — не в плане («Not today»), 0 —
/// Unassigned, N — дорожка N (как в <see cref="LanePlanEvent"/>).
/// </summary>
public static class LanePlanEventRules
{
    /// <summary>
    /// Сколько после прошлого переноса того же человека тем же тренером новый перенос не заводит
    /// новую строку, а правит прошлую: редактор сохраняет каждое перетаскивание, и «таскал Ноа
    /// туда-сюда минуту» иначе дало бы пять строк. Вернул на место — строка исчезает.
    /// </summary>
    public static readonly TimeSpan MergeWindow = TimeSpan.FromMinutes(10);

    /// <summary>Место из записи плана: нет записи — null, без дорожки — 0, иначе номер дорожки.</summary>
    public static int? Place(bool inPlan, int? laneNo) => !inPlan ? null : laneNo ?? 0;

    /// <summary>Место из <see cref="LanePlaceDto"/> (сдвиг по ответу и его Undo).</summary>
    public static int? Place(LanePlaceDto place) => Place(place.InPlan, place.LaneNo);

    /// <summary>Что сделать с переносом тренера, если у человека уже есть строка ленты.</summary>
    public enum MoveMerge
    {
        /// <summary>Новая строка.</summary>
        Add,

        /// <summary>Поправить «куда» и время у прошлой строки.</summary>
        Update,

        /// <summary>Прошлую строку убрать: человек вернулся туда, откуда его переносили.</summary>
        Remove,
    }

    /// <summary>
    /// Перенос тренером: склеить с прошлой строкой этого человека, если она — тоже перенос тем же
    /// тренером не дольше <see cref="MergeWindow"/> назад. Ответы участников не склеиваются.
    /// </summary>
    public static MoveMerge MergeCoachMove(LanePlanEvent? last, int? actorUserId, int? to, DateTime now)
    {
        if (last == null || last.Kind != LanePlanEventKind.Moved || last.ActorUserId != actorUserId
            || now - last.At > MergeWindow)
            return MoveMerge.Add;
        return last.FromLane == to ? MoveMerge.Remove : MoveMerge.Update;
    }
}
