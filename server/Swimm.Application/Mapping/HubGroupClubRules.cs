using System.Globalization;
using System.Text;
using Swimm.Domain;
using Swimm.Domain.Entities;

namespace Swimm.Application.Mapping;

/// <summary>
/// Правила подписки группы на клуб (docs/plans/hubgroup-club-subscription-plan.md §2) — одно
/// место. Здесь только решения, без БД: кто считается пловцом клуба, что пересборка добавляет и
/// убирает, чем кончается склейка двух строк одной группы, когда имя группы «занято» клубом.
/// Второй копии этих правил быть не должно — пересборка, отписка, склейка пловцов, валидация
/// имени и одобрение официальной группы зовут одно и то же.
/// </summary>
public static class HubGroupClubRules
{
    // ── Имя группы рядом с официальной группой клуба (П4) ────────────────────

    /// <summary>
    /// Дописывается к имени неофициальной группы, совпавшему с именем клуба или его официальной
    /// группы, в момент одобрения официальной (решение Влада §1-4). Меняет только отображаемое
    /// имя: slug прежний, иначе «доступна по ссылке» сломалась бы в ту же секунду.
    /// </summary>
    public const string CommunitySuffix = " · community";

    /// <summary>
    /// Имя для сравнения «совпадает ли с клубом» (§2): регистр, огласовки иврита, кавычки и
    /// гереши, дефисы/точки/запятые, крайние и двойные пробелы. «איל"ן» и «אילן», «Dolphine-Netanya»
    /// и «dolphine netanya» — одно имя. Похожие имена («… Fans») сознательно НЕ ловим — это админу.
    /// </summary>
    public static string NormalizeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";

        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            // Огласовки и кантилляция иврита — метки без собственной ширины: выбрасываем.
            if (c is >= HebrewMarksFrom and <= HebrewMarksTo
                && CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                continue;

            switch (c)
            {
                // Кавычки и гереши выбрасываем: «איל"ן» пишут и «אילן».
                case '"' or '\'' or Geresh or Gershayim or LeftSingleQuote or RightSingleQuote
                    or LeftDoubleQuote or RightDoubleQuote:
                    continue;
                // Разделители слов (включая макаф и тире) — в пробел.
                case '-' or '.' or ',' or Maqaf or EnDash or EmDash:
                    sb.Append(' ');
                    continue;
            }

            sb.Append(char.IsWhiteSpace(c) ? ' ' : char.ToLowerInvariant(c));
        }

        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    // Спецсимволы — кодами: литералом в исходнике огласовку не видно, а редакторы их портят.
    private const char HebrewMarksFrom = (char)0x0591;   // кантилляция…
    private const char HebrewMarksTo = (char)0x05C7;     // …и огласовки (метки внутри — NonSpacingMark)
    private const char Maqaf = (char)0x05BE;             // ивритский дефис
    private const char Geresh = (char)0x05F3;
    private const char Gershayim = (char)0x05F4;
    private const char LeftSingleQuote = (char)0x2018;
    private const char RightSingleQuote = (char)0x2019;
    private const char LeftDoubleQuote = (char)0x201C;
    private const char RightDoubleQuote = (char)0x201D;
    private const char EnDash = (char)0x2013;
    private const char EmDash = (char)0x2014;

    /// <summary>Совпадает ли имя с каким-нибудь из занятых (после нормализации). Пустое не совпадает ни с чем.</summary>
    public static bool ConflictsWith(string? name, IEnumerable<string?> reservedNames)
    {
        var normalized = NormalizeName(name);
        return normalized.Length > 0 && reservedNames.Any(r => NormalizeName(r) == normalized);
    }

    /// <summary>Имя с суффиксом « · community», не длиннее <paramref name="maxLength"/> (колонка — 200).</summary>
    public static string WithCommunitySuffix(string name, int maxLength)
    {
        var trimmed = name.Trim();
        var keep = Math.Min(trimmed.Length, maxLength - CommunitySuffix.Length);
        return trimmed[..keep].TrimEnd() + CommunitySuffix;
    }

    /// <summary>
    /// Отказ сохранить неофициальной группе имя клуба, у которого есть официальная группа
    /// (§2: до официальной имя клуба разрешено, после — нет). Предлагает рабочий вариант.
    /// </summary>
    public static string NameTakenError(string clubName, string attemptedName) =>
        $"This name belongs to {Isolate(clubName)}, which has an official group. " +
        $"Try “{Isolate(attemptedName.Trim() + CommunitySuffix)}”.";

    /// <summary>Плашка в «My groups» у группы, которую официальная группа клуба убрала из каталога.</summary>
    public static string NotInCatalogNotice(string clubName, string officialGroupName) =>
        $"Not in the catalog: {Isolate(clubName)} has an official group — {Isolate(officialGroupName)}. " +
        "Your group still works by its link.";
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
