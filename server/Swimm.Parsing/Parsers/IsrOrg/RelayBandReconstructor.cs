using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Swimm.Parsing.Helpers;
using Swimm.Parsing.Models;

namespace Swimm.Parsing.Parsers.IsrOrg;

/// <summary>
/// Реконструкция зачётных полос эстафет, которые протокол печатает БЕЗ пола и возраста
/// (Маккаби: заголовок «4X50 חופשי שליחים», места сквозные по времени всей дисциплины).
/// Организатор считает клубный зачёт по полосам «возрастная группа × пол» — полосы есть
/// в его системе заявок, но в PDF не печатаются. Без реконструкции очки эстафет получает
/// только топ-20 сквозного списка (старшие полосы), и клубный зачёт не сходится с
/// официальной таблицей (сверка Маккаби-2026 «цеирим», comp 1565).
///
/// Полоса восстанавливается ТОЛЬКО из самого файла — как HeatType в
/// <see cref="IsrOrgParser.AssignHeatTypes"/>:
/// <list type="bullet">
/// <item>возрастная группа — по СТАРШЕЙ ноге состава (возраст = год соревнования минус год
/// рождения, как <c>DetermineAge</c> у индивидуальных строк того же файла);</item>
/// <item>пол команды — по полу ног, а пол ноги известен из ИНДИВИДУАЛЬНЫХ заплывов того же
/// протокола (точное имя+год в полосе «בנות/בנים»), с фолбэком на однозначное по полу имя.</item>
/// </list>
/// Детерминированность от файла принципиальна: <c>Gender</c> входит в ключ upsert
/// (<c>ResultMatcher</c>), и пол, зависящий от состояния БД, на переимпорте плодил бы
/// дубликаты (инцидент И-4). Здесь же каждый переимпорт того же файла даёт те же полосы.
///
/// Всё или ничего: если хоть у одной команды дисциплины полоса не восстанавливается,
/// дисциплина остаётся сквозной, как напечатана, — частично разбитые полосы дали бы
/// кривые места. Настоящие смешанные эстафеты сюда не попадают: у них пол в заголовке
/// («מיקס» → gender "mix", шабатные — EventStyleAge "shabbat").
/// </summary>
internal static class RelayBandReconstructor
{
    /// <summary>
    /// Метка пола для ивритского названия полосного события. У открытых сверху полос
    /// («16-99») организатор пишет взрослое נשים/גברים, у детских — בנות/בנים; повторяем,
    /// чтобы название совпадало с событием в live-зачёте loglig.
    /// </summary>
    private static string HeGenderLabel(string gender, string band)
    {
        var adult = band.EndsWith("-99", StringComparison.Ordinal);
        return gender == "male"
            ? (adult ? "גברים" : "בנים")
            : (adult ? "נשים" : "בנות");
    }

    /// <summary>
    /// Разбивает эстафетные дисциплины «без категории» (gender none, возраст пуст) на полосы
    /// с пересчётом мест внутри полосы. Остальные события возвращаются как есть, на своих местах.
    /// </summary>
    internal static List<IsrOrgCompetitionResult> Reconstruct(IReadOnlyList<IsrOrgCompetitionResult> comps)
    {
        var (exactGender, firstNameGender) = BuildGenderIndex(comps);

        // Составы всех кандидатов считаем ДО разбиения: по ним выбирается сетка полос,
        // общая на весь файл (см. PickGrid).
        var teams = comps
            .Select(c => IsCandidate(c) ? ClassifyTeams(c, exactGender, firstNameGender) : null)
            .ToList();
        var grid = PickGrid(teams);

        var result = new List<IsrOrgCompetitionResult>(comps.Count);
        for (var i = 0; i < comps.Count; i++)
        {
            var bands = teams[i] is null ? null : TrySplit(comps[i], teams[i]!, grid);
            if (bands is null)
                result.Add(comps[i]);   // полосы не восстановились — оставляем сквозной протокол
            else
                result.AddRange(bands); // полосные события на месте исходного
        }
        return result;
    }

    /// <summary>
    /// Маркер чемпионата Маккаби в названии протокола. Сетки полос ниже — из регламента
    /// МАККАБИ, чужому протоколу они бы выдумали неверные полосы. Эстафеты «без категории»
    /// есть и у ARENA (например «бугрим» 1562, «צעירים» 1512) — там организатор в зачёте
    /// полос не делает, и трогать их нельзя.
    /// </summary>
    private const string MaccabiTitleMarker = "אליפות מכבי";

    /// <summary>Эстафетная дисциплина, у которой в заголовке не было ни пола, ни возраста.</summary>
    private static bool IsCandidate(IsrOrgCompetitionResult comp) =>
        comp.Competition.Contains(MaccabiTitleMarker, StringComparison.Ordinal)
        && comp.EventStyleGender == "none"
        && string.IsNullOrEmpty(comp.EventStyleAge)
        && comp.Results.Count > 0
        && comp.Results.All(r => r.IsRelay == true && r.RelaySwimmers is { Count: > 0 });

    /// <summary>
    /// Индексы пола по индивидуальным строкам файла: точный ключ «токены имени + год рождения»
    /// и словарь имён, встречающихся только у одного пола. Ключ — НАБОР токенов, потому что
    /// порядок «фамилия имя» у ноги эстафеты и у индивидуальной строки может не совпадать.
    /// </summary>
    private static (Dictionary<string, string> Exact, Dictionary<string, string> FirstName)
        BuildGenderIndex(IReadOnlyList<IsrOrgCompetitionResult> comps)
    {
        var exact = new Dictionary<string, string>();
        var ambiguousExact = new HashSet<string>();
        var firstNameGenders = new Dictionary<string, HashSet<string>>();

        foreach (var comp in comps)
        {
            if (comp.EventStyleGender is not ("male" or "female")) continue;
            foreach (var r in comp.Results)
            {
                if (r.IsRelay == true) continue;
                var key = NameKey($"{r.LastName} {r.FirstName}", r.BirthYear);
                if (key is null) continue;

                if (exact.TryGetValue(key, out var known) && known != comp.EventStyleGender)
                    ambiguousExact.Add(key);
                else
                    exact.TryAdd(key, comp.EventStyleGender);

                foreach (var token in Tokens(r.FirstName))
                {
                    if (!firstNameGenders.TryGetValue(token, out var set))
                        firstNameGenders[token] = set = [];
                    set.Add(comp.EventStyleGender);
                }
            }
        }

        foreach (var key in ambiguousExact) exact.Remove(key);
        var firstName = firstNameGenders
            .Where(kv => kv.Value.Count == 1)
            .ToDictionary(kv => kv.Key, kv => kv.Value.Single());
        return (exact, firstName);
    }

    private static IEnumerable<string> Tokens(string? name) =>
        (name ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Ключ имени: токены в каноническом порядке + год рождения; null — без года.</summary>
    private static string? NameKey(string? fullName, int birthYear)
    {
        if (birthYear <= 0) return null;
        var tokens = Tokens(fullName).OrderBy(t => t, StringComparer.Ordinal).ToList();
        return tokens.Count == 0 ? null : $"{string.Join(' ', tokens)}|{birthYear}";
    }

    /// <summary>
    /// Состав команд дисциплины: пол и возраст СТАРШЕЙ ноги — всё, что нужно для полосы.
    /// null — хоть одна команда не классифицировалась (пол или год рождения не восстановлены),
    /// дисциплину не трогаем.
    /// </summary>
    private static List<(IsrOrgResult Row, string Gender, int TopAge)>? ClassifyTeams(
        IsrOrgCompetitionResult comp,
        Dictionary<string, string> exactGender,
        Dictionary<string, string> firstNameGender)
    {
        // Возраст — по сезону (год окончания), как нарезаны полосы протокола:
        // календарный счёт на осенних стартах даёт год младше (docs/season-boundary-rule.md).
        var eventYear = AgeGroupHelper.SeasonEndYearFromDateString(comp.Date);
        var teams = new List<(IsrOrgResult, string, int)>(comp.Results.Count);

        foreach (var row in comp.Results)
        {
            var gender = TeamGender(row.RelaySwimmers!, exactGender, firstNameGender);
            if (gender is null) return null;

            var ages = row.RelaySwimmers!
                .Where(s => s.BirthYear is > 0)
                .Select(s => eventYear - s.BirthYear!.Value)
                .Where(a => a > 0)
                .ToList();
            if (ages.Count == 0) return null;

            teams.Add((row, gender, ages.Max()));
        }
        return teams;
    }

    /// <summary>
    /// Пытается разбить дисциплину на полосы выбранной сеткой. null — хоть одна команда
    /// в сетку не попала, дисциплину не трогаем.
    /// </summary>
    private static List<IsrOrgCompetitionResult>? TrySplit(
        IsrOrgCompetitionResult comp,
        List<(IsrOrgResult Row, string Gender, int TopAge)> teams,
        RelayBandGrid grid)
    {
        var classified = new List<(IsrOrgResult Row, string Band, int BandLowerAge, string Gender)>();

        foreach (var (row, gender, topAge) in teams)
        {
            // Полоса — по СТАРШЕМУ участнику; сетка полос выбрана на весь файл, см. PickGrid.
            var band = MaccabiRelayBand(gender, topAge, grid);
            if (band is null) return null;
            classified.Add((row, band, BandLowerAge(band), gender));
        }

        return classified
            // Порядок полос фиксированный (старшие → младшие, мальчики → девочки): от него
            // зависит порядок строк в файле импорта, а значит FIFO-доматчинг upsert.
            .GroupBy(t => (t.Band, t.BandLowerAge, t.Gender))
            .OrderByDescending(g => g.Key.BandLowerAge)
            .ThenBy(g => g.Key.Gender == "male" ? 0 : 1)
            .Select(g => BandEvent(comp, g.Key.Band, g.Key.Gender, g.Select(t => t.Row).ToList()))
            .ToList();
    }

    /// <summary>
    /// Пол команды. Точные матчи (имя+год плыл индивидуально в этом же файле) главнее
    /// фолбэка по имени: унисекс-имена (ג'וד…) через фолбэк голосуют неверно, и одна такая
    /// нога не должна перевешивать опознанных участников. Правила:
    /// есть точные и они единогласны → их пол; точных нет — минимум два единогласных
    /// голоса по имени; иначе пол не определён.
    /// </summary>
    private static string? TeamGender(
        IReadOnlyList<RelaySwimmer> legs,
        Dictionary<string, string> exactGender,
        Dictionary<string, string> firstNameGender)
    {
        var exactVotes = new HashSet<string>();
        var nameVotes = new List<string>();

        foreach (var leg in legs)
        {
            var key = NameKey($"{leg.LastName} {leg.FirstName}", leg.BirthYear ?? 0);
            if (key is not null && exactGender.TryGetValue(key, out var g))
            {
                exactVotes.Add(g);
                continue;
            }

            // Фолбэк: любой токен имени ноги, известный словарю имён (раскладка Last/First
            // у ноги ненадёжна). Токены с разными полами взаимно гасятся.
            var tokenGenders = Tokens($"{leg.LastName} {leg.FirstName}")
                .Where(firstNameGender.ContainsKey)
                .Select(t => firstNameGender[t])
                .Distinct()
                .ToList();
            if (tokenGenders.Count == 1) nameVotes.Add(tokenGenders[0]);
        }

        if (exactVotes.Count == 1) return exactVotes.Single();
        if (exactVotes.Count == 0 && nameVotes.Count >= 2 && nameVotes.Distinct().Count() == 1)
            return nameVotes[0];
        return null;
    }

    /// <summary>Сетка эстафетных полос: у Маккаби их две — по возрастной половине чемпионата.</summary>
    private enum RelayBandGrid
    {
        /// <summary>«צעירים» (loglig doc 3185): девочки 9-11 и 12-13, мальчики 9-10, 11-12, 13-14.</summary>
        Youth,

        /// <summary>«נוער ובוגרים»: בנות 14-15, בנים 15-16, נשים 16-99, גברים 17-99.</summary>
        Senior
    }

    /// <summary>
    /// Сетка выбирается на ВЕСЬ файл и с приоритетом младшей: берём Youth, если по ней
    /// раскладываются все команды всех дисциплин файла, иначе Senior. Так «цеирим»
    /// (все возрасты ≤14) считается ровно как раньше, а файл с юношами и взрослыми —
    /// по своей сетке. Выбор от файла, а не от БД, — то же требование
    /// детерминированности, что и у пола (инцидент И-4).
    /// </summary>
    private static RelayBandGrid PickGrid(
        IReadOnlyList<List<(IsrOrgResult Row, string Gender, int TopAge)>?> teamsPerComp)
    {
        var youthFits = teamsPerComp
            .Where(t => t is not null)
            .SelectMany(t => t!)
            .All(t => MaccabiRelayBand(t.Gender, t.TopAge, RelayBandGrid.Youth) is not null);
        return youthFits ? RelayBandGrid.Youth : RelayBandGrid.Senior;
    }

    /// <summary>
    /// Полоса Маккаби по полу и возрасту старшей ноги. Обе сетки сверены с live-зачётом
    /// loglig: Youth — соревнование 14668 (comp 1565), Senior — 14669 (comp 1576), где
    /// эстафетные события ровно «בנות 14-15 / בנים 15-16 / נשים 16-99 / גברים 17-99».
    /// У девочек полосы ШИРЕ стандартной сетки возрастных групп, у старших они ещё и
    /// открытые сверху. Возраст вне сетки — null: дисциплина остаётся сквозной, как
    /// напечатана (страховка от чужих форматов).
    /// </summary>
    private static string? MaccabiRelayBand(string gender, int topAge, RelayBandGrid grid)
    {
        if (topAge < 9) return null;
        if (grid == RelayBandGrid.Youth)
        {
            if (gender == "female")
                return topAge <= 11 ? "9-11" : topAge <= 13 ? "12-13" : null;
            return topAge <= 10 ? "9-10" : topAge <= 12 ? "11-12" : topAge <= 14 ? "13-14" : null;
        }

        if (gender == "female")
            return topAge <= 15 ? "14-15" : "16-99";
        return topAge <= 16 ? "15-16" : "17-99";
    }

    /// <summary>Нижняя граница полосы («13-14» → 13) — для сортировки полос по старшинству.</summary>
    private static int BandLowerAge(string band)
    {
        var m = Regex.Match(band, @"^(\d+)");
        return m.Success ? int.Parse(m.Value) : 0;
    }

    /// <summary>
    /// Полосное событие: места пересчитаны внутри полосы по времени (равные времена делят
    /// место, следующий его пропускает — как в протоколах). Команды без времени (DQ/NS/DNF)
    /// идут в конец без места, как у индивидуальных дисквалификаций.
    /// </summary>
    private static IsrOrgCompetitionResult BandEvent(
        IsrOrgCompetitionResult comp, string band, string gender, List<IsrOrgResult> rows)
    {
        var timed = rows
            .Where(r => TimeMs(r.Time) is not null)
            .OrderBy(r => TimeMs(r.Time)!.Value)
            .ThenBy(r => r.Position is int p ? p : int.MaxValue)
            .ToList();
        var untimed = rows
            .Where(r => TimeMs(r.Time) is null)
            .OrderBy(r => r.Position is int p ? p : int.MaxValue)
            .ToList();

        var reRanked = new List<IsrOrgResult>(rows.Count);
        long? prevMs = null;
        var prevPlace = 0;
        for (var i = 0; i < timed.Count; i++)
        {
            var ms = TimeMs(timed[i].Time)!.Value;
            var place = ms == prevMs ? prevPlace : i + 1;
            prevMs = ms;
            prevPlace = place;
            reRanked.Add(timed[i] with { Position = place });
        }
        reRanked.AddRange(untimed.Select(r => r with { Position = null }));

        return comp with
        {
            AgeGroup = band,
            Event = $"{comp.Event} - {HeGenderLabel(gender, band)} {band}",
            EventStyleGender = gender,
            EventStyleAge = band,
            Results = reRanked
        };
    }

    /// <summary>Время «[чч:]мм:сс.дд» в миллисекунды; null — нет времени (DQ/NS/DNF).</summary>
    private static long? TimeMs(string? time)
    {
        if (string.IsNullOrWhiteSpace(time)) return null;
        var m = Regex.Match(time.Trim(), @"^(?:(\d{1,2}):)?(\d{1,2}):(\d{2})\.(\d{1,2})$");
        if (!m.Success) return null;

        var hours = m.Groups[1].Success ? int.Parse(m.Groups[1].Value) : 0;
        var minutes = int.Parse(m.Groups[2].Value);
        var seconds = int.Parse(m.Groups[3].Value);
        var frac = m.Groups[4].Value;
        var ms = int.Parse(frac) * (frac.Length == 1 ? 100 : 10);
        return ((hours * 60L + minutes) * 60 + seconds) * 1000 + ms;
    }
}
