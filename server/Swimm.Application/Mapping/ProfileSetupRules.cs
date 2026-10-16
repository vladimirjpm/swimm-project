namespace Swimm.Application.Mapping;

/// <summary>
/// Что уже есть у пользователя из трёх шагов «Set up profile». Считает репозиторий
/// (EXISTS-запросы), правило только складывает.
/// </summary>
/// <param name="HasMe">Есть пловец со звездой «это я» (<c>UserFavorite.IsPrimary</c>).</param>
/// <param name="HasClub">Есть хотя бы один клуб в избранном (несколько клубов — норма, Р6).</param>
/// <param name="InGroup">Состоит в группе как аккаунт — <c>active</c> ИЛИ <c>pending</c> (Р3).</param>
public sealed record ProfileSetupFacts(bool HasMe, bool HasClub, bool InGroup);

/// <summary>
/// От каких шагов пользователь отказался («Not a swimmer», «No club», «No group») —
/// колонки <c>AppUser.ProfileSkip*</c>.
/// </summary>
public sealed record ProfileSetupSkips(bool Me, bool Club, bool Group)
{
    public static readonly ProfileSetupSkips None = new(false, false, false);
}

/// <summary>
/// Шаг «Set up profile»: ключ (<see cref="ProfileSetupRules.StepMe"/>…), засчитан ли и засчитан ли
/// ОТКАЗОМ. <c>Skipped</c> — только когда шаг не сделан делом: сделанное сильнее отказа.
/// </summary>
public sealed record ProfileSetupStep(string Key, bool Done, bool Skipped = false);

/// <summary>Итог для пункта меню и страницы /setup-profile.</summary>
public sealed record ProfileSetupStatus(IReadOnlyList<ProfileSetupStep> Steps, int Done, int Total, bool Visible);

/// <summary>
/// Шаги и счётчик пункта «Set up profile» (docs/plans/profile-setup-plan.md, хендофф 1a).
/// Единственное место, где решается, что показать: его зовут и пункт меню, и страница — разъедутся
/// копии, и меню покажет «2/3» там, где страница уже «готово».
///
/// Решения Влада 16.10.2026:
/// - шагов ровно три и всегда одни и те же: «это я», клуб, группа;
/// - опрос «Who are you?» (<c>AppUser.ProfileRole*</c>) ни на что не влияет — поэтому его здесь
///   нет вовсе, ни во входе, ни в счётчике;
/// - заявка в группу, ждущая одобрения, засчитывается: со стороны пользователя шаг сделан;
/// - Dismiss прячет пункт, даже если шаги не доделаны.
///
/// Решение Влада 17.10.2026: от шага можно отказаться — тренер отмечает только клуб, родитель
/// обходится без «это я». Отказ засчитывает шаг так же, как сделанный, и снимается в любой момент.
/// </summary>
public static class ProfileSetupRules
{
    public const string StepMe = "me";
    public const string StepClub = "club";
    public const string StepGroup = "group";

    /// <summary>Ключ шага из адреса (<c>…/steps/{key}/skip</c>) — один из трёх.</summary>
    public static bool IsStepKey(string? key) => key is StepMe or StepClub or StepGroup;

    public static ProfileSetupStatus Evaluate(ProfileSetupFacts facts, bool dismissed) =>
        Evaluate(facts, ProfileSetupSkips.None, dismissed);

    public static ProfileSetupStatus Evaluate(ProfileSetupFacts facts, ProfileSetupSkips skips, bool dismissed)
    {
        var steps = new[]
        {
            Step(StepMe, facts.HasMe, skips.Me),
            Step(StepClub, facts.HasClub, skips.Club),
            Step(StepGroup, facts.InGroup, skips.Group),
        };

        var done = steps.Count(s => s.Done);
        return new ProfileSetupStatus(steps, done, steps.Length, Visible: !dismissed && done < steps.Length);
    }

    private static ProfileSetupStep Step(string key, bool fact, bool skip) =>
        new(key, Done: fact || skip, Skipped: !fact && skip);

    /// <summary>Код отказа 400: «Just watching» пришёл вместе с другой ролью.</summary>
    public const string WatcherExclusiveErrorCode = "watcher_exclusive";

    /// <summary>
    /// Ответ опроса допустим? «Just watching» исключает остальные (решение Влада 16.10.2026):
    /// выбрал его — остальные сбрасываются, выбрал другую роль — снимается он. Какую галочку
    /// нажали, знает только клиент, поэтому сервер не угадывает, а отказывает.
    /// </summary>
    public static bool IsValidSurvey(bool swimmer, bool parent, bool coach, bool watcher) =>
        !(watcher && (swimmer || parent || coach));
}
