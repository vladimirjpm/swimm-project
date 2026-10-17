namespace Swimm.Application.Mapping;

/// <summary>
/// Что уже есть у пользователя из шагов «Set up profile». Считает репозиторий
/// (EXISTS-запросы), правило только складывает.
/// </summary>
/// <param name="HasMe">Есть пловец со звездой «это я» (<c>UserFavorite.IsPrimary</c>).</param>
/// <param name="HasClub">Есть хотя бы один клуб в избранном (несколько клубов — норма, Р6).</param>
/// <param name="InGroup">Состоит в группе как аккаунт — <c>active</c> ИЛИ <c>pending</c> (Р3).</param>
/// <param name="HasFamily">Есть хотя бы один пловец с пометкой «семья» (<c>UserFavorite.IsFamily</c>).</param>
public sealed record ProfileSetupFacts(bool HasMe, bool HasClub, bool InGroup, bool HasFamily = false);

/// <summary>
/// От каких шагов пользователь отказался («Not a swimmer», «No club», «No group», «Skip» у
/// семьи) — колонки <c>AppUser.ProfileSkip*</c>.
/// </summary>
public sealed record ProfileSetupSkips(bool Me, bool Club, bool Group, bool Family = false)
{
    public static readonly ProfileSetupSkips None = new(false, false, false);
}

/// <summary>
/// Шаг «Set up profile»: ключ (<see cref="ProfileSetupRules.StepMe"/>…), засчитан ли и засчитан ли
/// ОТКАЗОМ. <c>Skipped</c> — только когда шаг не сделан делом: сделанное сильнее отказа.
/// </summary>
public sealed record ProfileSetupStep(string Key, bool Done, bool Skipped = false);

/// <summary>
/// Итог для пункта меню и страницы /setup-profile. <c>Visible</c> — показывать «Set up profile»
/// (иначе в меню тихий «My profile»); <c>Complete</c> — все шаги сделаны или пропущены;
/// <c>NewSteps</c> — несделанные шаги, которых пользователь ещё не видел (хендофф ex2, 3b).
/// </summary>
public sealed record ProfileSetupStatus(
    IReadOnlyList<ProfileSetupStep> Steps, int Done, int Total, bool Visible,
    bool Complete = false, IReadOnlyList<string>? NewSteps = null);

/// <summary>
/// Шаги и счётчик пункта «Set up profile» (docs/plans/profile-setup-plan.md, хендофф 1a).
/// Единственное место, где решается, что показать: его зовут и пункт меню, и страница — разъедутся
/// копии, и меню покажет «2/3» там, где страница уже «готово».
///
/// Решения Влада 16.10.2026:
/// - три базовых шага у всех и всегда одни и те же: «это я», клуб, группа;
/// - опрос «Who are you?» (<c>AppUser.ProfileRole*</c>) на них не влияет и в счётчик не входит
///   (единственное исключение — родитель и шаг семьи, см. ниже);
/// - заявка в группу, ждущая одобрения, засчитывается: со стороны пользователя шаг сделан;
/// - Dismiss прячет пункт, даже если шаги не доделаны.
///
/// Решение Влада 17.10.2026: от шага можно отказаться — тренер отмечает только клуб, родитель
/// обходится без «это я». Отказ засчитывает шаг так же, как сделанный, и снимается в любой момент.
///
/// Хендофф set-up-profile-ex3 (решение Влада 17.10.2026): опрос влияет на шаги ровно в одном
/// месте — «I'm a parent» добавляет шаг <see cref="StepFamily"/> (отметить детей семьёй, до
/// <c>FavoritesRules.MaxFamily</c>). Остальные роли шагов не дают. Сняли роль — шаг пропал,
/// а сохранённая семья осталась.
///
/// Хендофф set-up-profile-ex2 (3b): <see cref="StepKeys"/> — это и есть список обязательных полей
/// профиля. Пользователь запоминает, какие шаги видел (снимок при завершении или Dismiss,
/// <c>AppUser.ProfileStepsSeen</c>). Добавили шаг — у всех, кто его не видел и не сделал, пункт
/// «Set up profile» возвращается с «New: …», в том числе после Dismiss. Пропущенный шаг не
/// возвращает: отказ — тоже «закрыт».
/// </summary>
public static class ProfileSetupRules
{
    public const string StepMe = "me";
    public const string StepFamily = "family";
    public const string StepClub = "club";
    public const string StepGroup = "group";

    /// <summary>
    /// Обязательные шаги профиля по порядку (как в хендоффе ex3: это я, семья, клуб, группа).
    /// Новый шаг — новый ключ здесь (и факт в <see cref="ProfileSetupFacts"/>): тем, кто уже всё
    /// заполнил, он придёт как «New: …». <see cref="StepFamily"/> показывается только родителю.
    /// </summary>
    public static readonly IReadOnlyList<string> StepKeys = [StepMe, StepFamily, StepClub, StepGroup];

    /// <summary>Ключ шага из адреса (<c>…/steps/{key}/skip</c>) — один из списка.</summary>
    public static bool IsStepKey(string? key) => key != null && StepKeys.Contains(key);

    /// <summary>Снимок «какие шаги видел» → множество ключей; null/пусто — снимка не было.</summary>
    public static IReadOnlySet<string>? ParseSeen(string? stored) =>
        string.IsNullOrWhiteSpace(stored)
            ? null
            : stored.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet();

    /// <summary>Снимок для записи: все нынешние шаги.</summary>
    public static string SeenSnapshot() => string.Join(",", StepKeys);

    public static ProfileSetupStatus Evaluate(ProfileSetupFacts facts, bool dismissed) =>
        Evaluate(facts, ProfileSetupSkips.None, dismissed);

    /// <param name="parent">Ответ опроса «I'm a parent» — добавляет шаг «My family».</param>
    public static ProfileSetupStatus Evaluate(
        ProfileSetupFacts facts, ProfileSetupSkips skips, bool dismissed, IReadOnlySet<string>? seen = null,
        bool parent = false)
    {
        var steps = new List<ProfileSetupStep> { Step(StepMe, facts.HasMe, skips.Me) };
        if (parent) steps.Add(Step(StepFamily, facts.HasFamily, skips.Family));
        steps.Add(Step(StepClub, facts.HasClub, skips.Club));
        steps.Add(Step(StepGroup, facts.InGroup, skips.Group));

        var done = steps.Count(s => s.Done);
        var complete = done == steps.Count;
        // «Новый» — только у того, кто уже видел набор шагов (снимок есть): новичку все шаги
        // новые, и подсвечивать там нечего.
        var newSteps = seen == null
            ? []
            : steps.Where(s => !s.Done && !seen.Contains(s.Key)).Select(s => s.Key).ToList();

        // Dismiss прячет пункт — но не от шага, которого человек ещё не видел.
        var visible = !complete && (!dismissed || newSteps.Count > 0);
        return new ProfileSetupStatus(steps, done, steps.Count, visible, complete, newSteps);
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
