using Swimm.Domain;
using Swimm.Domain.Entities;

namespace Swimm.Application.Mapping;

/// <summary>
/// Правила подписки группы на клуб (docs/plans/hubgroup-club-subscription-plan.md §2) — одно
/// место. Здесь только решения, без БД: кто считается пловцом клуба, что пересборка добавляет и
/// убирает, чем кончается склейка двух строк одной группы. Второй копии этих правил быть не
/// должно — пересборка, отписка и склейка пловцов зовут одно и то же.
/// </summary>
public static class HubGroupClubRules
{
    /// <summary>
    /// Начало окна «пловцы клуба»: выступали за клуб в текущем ИЛИ прошлом сезоне. Прошлый —
    /// потому что сезон начинается осенью, и в начале сезона текущий почти пуст: состав
    /// обнулился бы до первого старта. Не <c>Swimmer.ClubId</c>: там и давно ушедшие.
    ///
    /// Границы календарные (<see cref="SeasonMath"/>); правило витрины «ждём зимнего
    /// чемпионата» здесь НЕ применяется — это окно активности, а не выбор сезона для показа.
    /// </summary>
    public static DateTime ActivitySince(DateTime now) =>
        SeasonMath.StartOf(SeasonMath.StartYearOf(now) - 1);

    /// <summary>Строка состава, как её видит пересборка.</summary>
    public sealed record MemberRow(int Id, int SwimmerId, string Source, bool IsExcluded);

    /// <summary>Что сделать с составом: каких пловцов вставить клубными, какие строки удалить.</summary>
    public sealed record SyncPlan(IReadOnlyList<int> InsertSwimmerIds, IReadOnlyList<int> DeleteRowIds)
    {
        public bool IsEmpty => InsertSwimmerIds.Count == 0 && DeleteRowIds.Count == 0;
    }

    /// <summary>
    /// План пересборки состава под целевой набор пловцов клуба.
    /// <list type="bullet">
    ///   <item>пловца набора без строки — вставить клубным;</item>
    ///   <item>строка уже есть (ручная, клубная, скрытая) — не трогать: ручной побеждает, а
    ///     скрытый владельцем НЕ возвращается;</item>
    ///   <item>клубную строку вне набора — удалить, скрытую тоже: скрытие без подписки на
    ///     этого пловца смысла не несёт;</item>
    ///   <item>ручные строки — никогда.</item>
    /// </list>
    /// Отписка = пустой набор: уходят все клубные строки, ручные остаются.
    /// </summary>
    public static SyncPlan PlanSync(IEnumerable<MemberRow> existing, IEnumerable<int> clubSwimmerIds)
    {
        var rows = existing.ToList();
        var target = clubSwimmerIds.ToHashSet();
        var present = rows.Select(r => r.SwimmerId).ToHashSet();

        var insert = target.Where(id => !present.Contains(id)).OrderBy(id => id).ToList();
        var delete = rows
            .Where(r => r.Source == HubGroupMemberSource.Club && !target.Contains(r.SwimmerId))
            .Select(r => r.Id)
            .ToList();

        return new SyncPlan(insert, delete);
    }

    // ── Тексты для витрины (по-английски — правило UI; план §4) ─────────────

    /// <summary>
    /// Изоляция имени внутри английской фразы (FSI … PDI): ивритское имя группы иначе утаскивает
    /// за собой соседнюю пунктуацию, и точка встаёт перед именем. Символы — кодами, а не
    /// escape-записью в исходнике: редакторы превращают её в невидимые символы.
    /// </summary>
    private static string Isolate(string name) =>
        $"{(char)0x2068}{name}{(char)0x2069}";

    /// <summary>
    /// Предупреждение при подписке: что будет с группой, когда у клуба есть или появится
    /// официальная группа. null — подписывается сама официальная группа клуба, ей нечего бояться.
    /// </summary>
    public static string SubscribeWarning(string? officialGroupName) => officialGroupName == null
        ? "This club has no official group yet. If one is approved, your group will leave the catalog — " +
          "it stays available by link — and can't use the club's name."
        : $"This club already has an official group: {Isolate(officialGroupName)}. " +
          "Your group will be available by link only.";

    /// <summary>
    /// Подсказка «вступить в существующую», а не запрет (решение Влада §1-3): у клуба уже есть
    /// группа — официальная или просто подписанная.
    /// </summary>
    public static string JoinInsteadHint(string groupName, bool isOfficial) => isOfficial
        ? $"Join the official group {Isolate(groupName)} instead?"
        : $"A group already follows this club: {Isolate(groupName)}. Join it instead?";

    /// <summary>
    /// Две строки одной группы после склейки пловцов-дублей сходятся в одну. Ручная побеждает —
    /// и тогда пловец видим: владелец выбрал его сам, а «ручной и скрытый» не бывает
    /// (CK_HubGroupMembers_ExcludedOnlyClub). Обе клубные — скрыт, если скрыта хоть одна:
    /// владелец уже сказал, что не хочет видеть этого человека.
    /// </summary>
    public static (string Source, bool IsExcluded) MergeRows(
        (string Source, bool IsExcluded) a, (string Source, bool IsExcluded) b)
    {
        if (a.Source == HubGroupMemberSource.Manual || b.Source == HubGroupMemberSource.Manual)
            return (HubGroupMemberSource.Manual, false);

        return (HubGroupMemberSource.Club, a.IsExcluded || b.IsExcluded);
    }
}
