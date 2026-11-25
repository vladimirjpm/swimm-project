namespace Swimm.Infrastructure.Services.DataChecks;

/// <summary>
/// Почему у заявки нет результата — причина на каждую заявку находки «Заявки без результата»
/// (<c>entries.no-show-unmatched</c>). Просьба Влада 25.11.2026: одно число «без результата 8
/// из 533» не говорит, честная это неявка или промах импорта, — а различить можно по базе, без
/// сети: кто стоит в заплыве и дорожке заявки.
///
/// Разбор 25.11.2026 (compID 16899, 16903, 16911, 16924, 16926 — 18 заявок) дал пять случаев:
/// <list type="bullet">
/// <item><b>двойник</b> — в его заплыве и дорожке «он же» под второй карточкой: имя то же,
///   короче или с опечаткой (ליבמן מילי ⊂ ליבמן מילי אריאל, נצ׳ייבקסי/נצ׳ייבסקי), год бывает
///   разный — одна из карточек ошибается. Лечится склейкой;</item>
/// <item><b>тёзка</b> — то же имя, но у обеих карточек свои, разные loglig-id (אדרי איתי #6663 и
///   #61243): это два человека, строка прицепилась не к тому. Лечится переносом строки, НЕ
///   склейкой;</item>
/// <item><b>замена в эстафете</b> — команда плыла, но без него;</item>
/// <item><b>дорожку занял другой</b> — со своей заявкой (переставили посев) или без неё
///   (замена либо ошибка имени в протоколе);</item>
/// <item><b>пусто</b> — в его заплыве и дорожке никого: честная неявка.</item>
/// </list>
/// Сверх того — <b>плыл в другом заплыве</b>: строка пловца в той же дисциплине есть, но сшивка
/// её не взяла (переставили в день старта, а кандидатов несколько).
///
/// Чистая функция: данные собирает <see cref="NoShowUnmatchedCheck"/>.
/// </summary>
public static class NoShowReasons
{
    public enum Kind { Twin, Namesake, SwamElsewhere, RelaySubstitution, LaneTakenSeed, LaneTakenNoEntry, Empty }

    /// <summary>Короткое имя случая — для строки-сводки находки.</summary>
    public static string Label(Kind kind) => kind switch
    {
        Kind.Twin => "двойник пловца",
        Kind.Namesake => "строка у тёзки",
        Kind.SwamElsewhere => "плыл в другом заплыве",
        Kind.RelaySubstitution => "замена в эстафете",
        Kind.LaneTakenSeed => "дорожку занял другой по заявке",
        Kind.LaneTakenNoEntry => "в дорожке другой без заявки",
        _ => "дорожка пуста — честная неявка",
    };

    /// <summary>
    /// Честный случай — смотреть нечего: неявка настоящая (пусто, замена в эстафете, посев
    /// переставили) или результат есть, просто сшивка его не взяла. Соревнование, где ВСЕ
    /// неявки честные, проверка принимает сама (решение Влада 25.11.2026). Двойник, тёзка и
    /// «другой без заявки» (замена или ошибка имени в протоколе) — человеку.
    /// </summary>
    public static bool IsHonest(Kind kind) =>
        kind is Kind.Empty or Kind.RelaySubstitution or Kind.LaneTakenSeed or Kind.SwamElsewhere;

    /// <summary>Пловец карточки (заявки или строки результата).</summary>
    public sealed record Person(int Id, string LastName, string FirstName, int BirthYear, int? LogligId, int? ClubId)
    {
        public string Name => $"{LastName} {FirstName}".Trim();
    }

    /// <summary>Заявка без результата.</summary>
    public sealed record Entry(Person Swimmer, int ClubId, string Gender, bool IsRelay, int Heat, int Lane);

    /// <summary>Строка результата в заплыве и дорожке заявки (та же дисциплина и день).</summary>
    /// <param name="HasOwnEntry">У пловца строки своя заявка в этой дисциплине.</param>
    /// <param name="RelayMembers">Состав команды (для эстафеты).</param>
    public sealed record LaneRow(Person Swimmer, int RowClubId, string? Gender, string? Time, bool HasOwnEntry,
        IReadOnlyList<Person> RelayMembers);

    /// <summary>Строка самого пловца в той же дисциплине в другом заплыве или дорожке.</summary>
    public sealed record OwnRow(int Heat, int Lane, string? Time);

    public sealed record Reason(Kind Kind, string Text);

    public static Reason Classify(Entry e, IReadOnlyList<LaneRow> lane, IReadOnlyList<OwnRow> own)
    {
        // Заплывы ♂ и ♀ нумеруются отдельно: в «заплыв 3, дорожка 2» бывают и мальчик, и
        // девочка (16903). Чужой пол — не про эту заявку.
        var rows = lane.Where(r => SameGenderHeat(e.Gender, r.Gender)).ToList();

        if (e.IsRelay)
        {
            if (rows.Count == 0)
                return new(Kind.Empty, $"эстафета в заплыве {e.Heat}, дорожке {e.Lane} не плыла — честная неявка");
            // В дорожке бывает и мужская, и женская (а у «mixed»-заявки — любая) команда: ищем его
            // в каждой, прежде чем объявить заменой.
            if (rows.Any(r => r.RelayMembers.Any(m => m.Id == e.Swimmer.Id)))
                return new(Kind.SwamElsewhere, "в составе команды есть — сшивка не свела, проверить состав");
            var twin = rows.SelectMany(r => r.RelayMembers).FirstOrDefault(m => SameName(e.Swimmer, m));
            if (twin is not null)
                return TwinOrNamesake(e, twin, "в составе команды");
            var team = rows.FirstOrDefault(r => r.RelayMembers.Count > 0) ?? rows[0];
            return new(Kind.RelaySubstitution, "замена в эстафете — команда плыла без этого пловца (состав: "
                + string.Join(", ", team.RelayMembers.Select(m => m.Name)) + ")");
        }

        if (own.Count > 0)
            return new(Kind.SwamElsewhere, "плыл в другом заплыве — "
                + string.Join(", ", own.Select(o => $"заплыв {o.Heat}, дорожка {o.Lane}" + (string.IsNullOrEmpty(o.Time) ? "" : $" ({o.Time})")))
                + ": заявку с ним сшивка не свела");

        var same = rows.FirstOrDefault(r => r.Swimmer.Id != e.Swimmer.Id && SameName(e.Swimmer, r.Swimmer));
        if (same is not null)
            return TwinOrNamesake(e, same.Swimmer, "в заплыве и дорожке заявки", same.RowClubId, same.Time);

        var other = rows.FirstOrDefault(r => r.Swimmer.Id != e.Swimmer.Id);
        if (other is null)
            return new(Kind.Empty, $"в заплыве {e.Heat}, дорожке {e.Lane} никого — честная неявка");

        var who = $"#{other.Swimmer.Id} {other.Swimmer.Name}" + (string.IsNullOrEmpty(other.Time) ? "" : $" ({other.Time})");
        return other.HasOwnEntry
            ? new(Kind.LaneTakenSeed, $"дорожку занял {who} по своей заявке — переставили посев, честная неявка")
            : new(Kind.LaneTakenNoEntry, $"в дорожке {who} без своей заявки"
                + (other.RowClubId == e.ClubId ? ", тот же клуб" : "")
                + " — замена или ошибка имени в протоколе");
    }

    private static Reason TwinOrNamesake(Entry e, Person other, string where, int? rowClubId = null, string? time = null)
    {
        var who = $"#{other.Id} {other.Name} ({other.BirthYear})" + (string.IsNullOrEmpty(time) ? "" : $", {time}");

        // Две карточки с СВОИМИ разными loglig-id — два человека: строку переносить, а не клеить.
        if (e.Swimmer.LogligId is int a && other.LogligId is int b && a != b)
            return new(Kind.Namesake, $"{where} строка у тёзки {who}, у них разные loglig-id"
                + (rowClubId == e.ClubId && other.ClubId != e.ClubId ? " (клуб строки — его)" : "")
                + " — перенести строку на этого пловца");

        var year = other.BirthYear != e.Swimmer.BirthYear
            ? $"; год расходится: у заявки {e.Swimmer.BirthYear}, у второй карточки {other.BirthYear}"
            : "";
        return new(Kind.Twin, $"{where} тот же пловец под второй карточкой {who} — двойник, склеить{year}");
    }

    /// <summary>
    /// Двойник, которого можно склеить БЕЗ человека (решение Влада 25.11.2026). Все условия сразу:
    /// личная заявка, в её заплыве и дорожке (своего пола) ровно один «он же» под другой карточкой;
    /// год рождения совпал; имя то же или одно полнее другого (ליבמן מילי ⊂ ליבמן מילי אריאל), а
    /// опечатка в буквах — уже человеку; клуб строки — клуб заявки, и клубы карточек не спорят;
    /// loglig-id ровно у одной из двух карточек — она и остаётся (у обеих — это тёзки, ни у одной —
    /// не на что опереться). Возвращает (остаётся, уходит) или null.
    /// </summary>
    public static (int Canonical, int Duplicate)? ReliableTwin(Entry e, IReadOnlyList<LaneRow> lane, IReadOnlyList<OwnRow> own)
    {
        if (e.IsRelay || own.Count > 0) return null;
        var same = lane.Where(r => SameGenderHeat(e.Gender, r.Gender) && r.Swimmer.Id != e.Swimmer.Id
                                   && SameName(e.Swimmer, r.Swimmer)).ToList();
        if (same.Count != 1) return null;

        var row = same[0];
        var other = row.Swimmer;
        if (other.BirthYear != e.Swimmer.BirthYear) return null;
        var ta = Tokens(e.Swimmer);
        var tb = Tokens(other);
        if (!ta.All(tb.Contains) && !tb.All(ta.Contains)) return null;
        if (row.RowClubId != e.ClubId) return null;
        if (e.Swimmer.ClubId is int a && other.ClubId is int b && a != b) return null;
        if ((e.Swimmer.LogligId is null) == (other.LogligId is null)) return null;

        return e.Swimmer.LogligId is not null ? (e.Swimmer.Id, other.Id) : (other.Id, e.Swimmer.Id);
    }

    private static bool SameGenderHeat(string? entry, string? row) =>
        !IsGendered(entry) || !IsGendered(row) || string.Equals(entry, row, StringComparison.OrdinalIgnoreCase);

    private static bool IsGendered(string? g) => g is "male" or "female";

    /// <summary>
    /// То же имя с поправкой на то, как его пишут разные протоколы: порядок и разбиение на
    /// имя/фамилию (ליאונג אלעד / תום), полнота (מילי ⊂ מילי אריאל), опечатка в пару букв
    /// (נצ׳ייבקסי / נצ׳ייבסקי). Год не сравнивается: у двойников он бывает разным.
    /// </summary>
    public static bool SameName(Person a, Person b)
    {
        var ta = Tokens(a);
        var tb = Tokens(b);
        if (ta.Count == 0 || tb.Count == 0) return false;
        if (ta.All(tb.Contains) || tb.All(ta.Contains)) return true;
        var ja = string.Join(' ', ta.OrderBy(t => t, StringComparer.Ordinal));
        var jb = string.Join(' ', tb.OrderBy(t => t, StringComparer.Ordinal));
        // Одна-две буквы на имя длиннее 6 символов: перестановка двух букв — это 2 правки.
        return Math.Min(ja.Length, jb.Length) > 6 && SwimmerDedupService.Levenshtein(ja, jb, 2) <= 2;
    }

    private static HashSet<string> Tokens(Person p) =>
        SwimmerDedupService.Normalize($"{p.LastName} {p.FirstName}")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
}
