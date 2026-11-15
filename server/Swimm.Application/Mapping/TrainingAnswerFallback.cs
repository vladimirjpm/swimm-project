namespace Swimm.Application.Mapping;

/// <summary>
/// Ответ тренера за пловца достаётся аккаунту, который этим пловцом стал (решение 15 плана
/// trainings-coach-flow, CF4). Тренер отвечал за пловца без аккаунта (Р73, строка со
/// <c>SwimmerId</c>); потом человек завёл аккаунт и встал этим пловцом БЕЗ метки тренера —
/// отметил его «Me» / «Family», или админ сайта привязал (порядок — <c>HubGroupPersonResolver</c>).
/// Метка тренера переносит строки сама (<c>HubGroupUserService.MoveSwimmerAnswersToAccountAsync</c>),
/// а эти пути меняют чужие таблицы и о тренировках не знают. Без подмены строка пловца пропадает
/// из переклички (пловец «занят» аккаунтом), а аккаунт числится «не ответил» и получает
/// напоминание на занятие, куда его уже записали.
///
/// Поэтому при ЧТЕНИИ: у аккаунта нет своего ответа на занятие, а у его пловца есть — это и есть
/// ответ аккаунта (строка несёт <c>SetByUserId</c> тренера → «by coach»). Свой ответ аккаунта
/// главнее. Строка пловца остаётся в базе, пока аккаунт сам не ответит на это занятие
/// (<c>TrainingRsvpService.SetAsync</c> её тогда удаляет).
/// </summary>
public static class TrainingAnswerFallback
{
    /// <param name="byUser">Ответы аккаунтов на одно занятие: userId → ответ.</param>
    /// <param name="bySwimmer">Ответы за пловцов на то же занятие: swimmerId → ответ.</param>
    /// <param name="swimmerOf">Кем стоит аккаунт: userId → пловец состава или null.</param>
    /// <returns>Ответы аккаунтов с подставленными ответами их пловцов (новый словарь).</returns>
    public static Dictionary<int, T> WithSwimmerAnswers<T>(
        IReadOnlyDictionary<int, T> byUser,
        IReadOnlyDictionary<int, T> bySwimmer,
        IReadOnlyDictionary<int, int?> swimmerOf)
    {
        var result = new Dictionary<int, T>(byUser);
        foreach (var (userId, swimmerId) in swimmerOf)
        {
            if (swimmerId is not int sid || result.ContainsKey(userId)) continue;
            if (bySwimmer.TryGetValue(sid, out var answer)) result[userId] = answer;
        }
        return result;
    }
}
