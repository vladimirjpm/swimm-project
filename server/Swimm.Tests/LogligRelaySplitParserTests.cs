using System.Text.Json.Nodes;
using Swimm.Parsing.Parsers.IsrOrg;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// PDF промежуточных одной эстафетной дисциплины loglig (docs/relays.md, «Промежуточные
/// эстафет»). Фикстуры — зимний чемпионат мастерсов 2026 (loglig 13805): женская 4×50
/// комплекс (73056) и микст 4×50 комплекс (73037). Оттуда же кейс, с которого всё началось:
/// рекорд 45-49 50 на спине 30.25 — это первый этап женской эстафеты Гостомельской.
/// </summary>
public class LogligRelaySplitParserTests
{
    private static List<SplitRelayTeam> Parse(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Parsing", name);
        using var fs = File.OpenRead(path);
        return LogligRelaySplitParser.Parse(fs);
    }

    private static List<SplitRelayTeam> Women => Parse("loglig-split-relay-13805-73056-women-medley.pdf");
    private static List<SplitRelayTeam> Mixed => Parse("loglig-split-relay-13805-73037-mixed-medley.pdf");

    [Fact]
    public void WomenMedley_LeadOffBackstrokeIs30_25()
    {
        var team = Assert.Single(Women, t => t.Time == "02:34.67");
        Assert.Equal((2, 8), (team.Heat, team.Lane));
        Assert.Equal("מכבי עולם המים", team.Club);

        Assert.Equal(4, team.Legs.Count);
        var lead = team.Legs[0];
        Assert.Equal((1, "גוסטמלסקי", "אניה", 1981, "00:30.25"),
            (lead.Order, lead.LastName, lead.FirstName, lead.BirthYear, lead.SplitTime));
        // Фамилия из двух слов — граница колонок, а не первое слово.
        Assert.Equal(("ברילר גולן", "דגנית"), (team.Legs[2].LastName, team.Legs[2].FirstName));

        // Обрывки в 10 pt над и под строкой ноги («BEN» / «SHOHA» / «M»): при узком пороге
        // терялся верхний кусок и выходила «SHOHAM» — вторая карточка той же пловчихи.
        var shoham = Assert.Single(Women, t => t.Time == "02:28.56");
        Assert.Equal("BEN SHOHAM", shoham.Legs[3].LastName);
    }

    [Fact]
    public void WrappedNames_AreJoined()
    {
        var teams = Mixed;
        // Фамилия перенесена на строки ВЫШЕ и НИЖЕ ноги: «מרמור» / «סירוטה».
        var own = Assert.Single(teams, t => t.Time == "01:58.43");
        Assert.Equal("מרמור סירוטה", own.Legs[2].LastName);
        Assert.Equal(("גוסטמלסקי", "00:27.11"), (own.Legs[3].LastName, own.Legs[3].SplitTime));

        // Обрывок из одной буквы приклеивается к слову: «טיטינשנייד» + «ר».
        var shoham = Assert.Single(teams, t => t.Time == "02:17.37");
        Assert.Equal("טיטינשניידר", shoham.Legs[0].LastName);
        // Латиница читается слева направо, перенос «SHOHA» + «M».
        Assert.Equal(("BEN SHOHAM", "Shirli"), (shoham.Legs[3].LastName, shoham.Legs[3].FirstName));
    }

    [Fact]
    public void WrapAfterFinalLetter_IsNewWord()
    {
        // «רבינוביץ» / «בץ»: строка кончается конечной ץ — слово закончилось, это две части
        // фамилии, а не перенос посреди слова (иначе «רבינוביץבץ» — пловец-тень).
        var men = Parse("loglig-split-relay-13805-73027-men-freestyle.pdf");
        var team = Assert.Single(men, t => t.Time == "01:35.78");
        Assert.Equal(("רבינוביץ בץ", "טל", "00:24.18"),
            (team.Legs[1].LastName, team.Legs[1].FirstName, team.Legs[1].SplitTime));
    }

    [Fact]
    public void TeamSpanningPageBreak_KeepsItsLegs()
    {
        // Строка команды — последняя на странице 1, ноги — на странице 2.
        var team = Assert.Single(Mixed, t => t.Time == "01:58.43");
        Assert.Equal((1, 2), (team.Heat, team.Lane));
        Assert.Equal(new[] { "00:26.18", "00:29.23", "00:35.91", "00:27.11" }, team.Legs.Select(l => l.SplitTime));
    }

    // ── Этапы длиннее 50 м (10.10.2026, docs/relays.md) ──────────────────────
    // Фикстуры — טריילז "ארנה" קיץ 2026, loglig 14561: 4×100 вольным ж (82498) и м (82495),
    // 4×200 вольным ж (82500). Левое время строки ноги у них — последний 50-метровый отрезок,
    // а этап — разность нарастающих.

    [Fact]
    public void Freestyle4x100_LegIsWholeHundred_NotLastFifty()
    {
        var women = Parse("loglig-split-relay-14561-82498-women-free-4x100.pdf");
        var shoham = Assert.Single(women, t => t.Time == "04:21.81");
        Assert.Equal("מכבי שוהם", shoham.Club);
        // Отрезки по PDF: 30.38 + 32.77 | 31.06 + 35.0 | 32.21 + 34.48 | 31.30 + 34.61.
        Assert.Equal(new[] { "01:03.15", "01:06.06", "01:06.69", "01:05.91" }, shoham.Legs.Select(l => l.SplitTime));

        var men = Parse("loglig-split-relay-14561-82495-men-free-4x100.pdf");
        var weisgal = Assert.Single(men, t => t.Time == "03:32.69");
        Assert.Equal(new[] { "00:53.66", "00:55.64", "00:51.88", "00:51.51" }, weisgal.Legs.Select(l => l.SplitTime));
    }

    [Fact]
    public void Freestyle4x200_WrappedCumulativeCell_DoesNotBreakLegTime()
    {
        // Узкая колонка «нарастающее 150» переносит цифру на строку ниже: «01:33.6» + «3».
        var team = Assert.Single(Parse("loglig-split-relay-14561-82500-women-free-4x200.pdf"), t => t.Time == "08:21.11");
        Assert.Equal(new[] { "02:07.97", "02:05.83", "02:04.67", "02:02.64" }, team.Legs.Select(l => l.SplitTime));
    }

    [Fact]
    public void BrokenSourceRows_LegsWithoutTimes_NoGuessing()
    {
        // Зимний чемпионат 2025, loglig 13627, 4×100 вольным ж (72979): loglig напечатал
        // отрицательные отрезки («-1.-29:00») и перепутал нарастающие. Ноги остаются (имена
        // нужны), времён нет — угадывать порядок колонок не беремся.
        // Испорчены строки у 21 команды из 22 (22-я снята) — разность нарастающих при этом
        // всё равно складывается в итог, и прежний сторож «сумма = итог» пропускал этапы
        // вроде 00:00.28.
        var teams = Parse("loglig-split-relay-13627-72979-women-free-4x100-broken.pdf");
        var bialik = Assert.Single(teams, t => t.Time == "03:45.08");
        Assert.Equal(4, bialik.Legs.Count);
        Assert.All(teams, t => Assert.All(t.Legs, l => Assert.Null(l.SplitTime)));

        // В 4×200 испорчены 10 команд из 20 («-7.-36:00» у второго этапа 08:56.94) — пустые
        // ровно они, остальные этапы получают.
        var long200 = Parse("loglig-split-relay-14561-82500-women-free-4x200.pdf");
        Assert.All(Assert.Single(long200, t => t.Time == "08:56.94").Legs, l => Assert.Null(l.SplitTime));
        Assert.Equal(10, long200.Count(t => t.Legs.Count == 4 && t.Legs.All(l => l.SplitTime != null)));
    }

    [Theory]
    [InlineData("loglig-split-relay-14561-82498-women-free-4x100.pdf")]
    [InlineData("loglig-split-relay-14561-82495-men-free-4x100.pdf")]
    [InlineData("loglig-split-relay-14561-82500-women-free-4x200.pdf")]
    public void LongLegs_SumToTeamTime(string fixture)
    {
        // Этапы обязаны складываться в итог команды — у каждой команды с промежуточными.
        var teams = Parse(fixture).Where(t => t.Time != null && t.Legs.All(l => l.SplitTime != null)).ToList();
        Assert.NotEmpty(teams);
        Assert.All(teams, t => Assert.Equal(
            Swimm.Application.Mapping.SwimTime.ParseToMs(t.Time),
            t.Legs.Sum(l => Swimm.Application.Mapping.SwimTime.ParseToMs(l.SplitTime))));
    }

    [Fact]
    public void DisqualifiedOrSplitlessTeams_HaveLegsWithoutSplits()
    {
        // DQ: статус вместо времени, ноги есть (имена нужны при сборке эстафет целиком).
        var dq = Assert.Single(Women, t => t.Time is null);
        Assert.Equal("DQ", dq.Status);
        Assert.Equal(4, dq.Legs.Count);
        Assert.All(dq.Legs, l => Assert.Null(l.SplitTime));

        // Итог есть, промежуточных источник не дал — ноги без времён, без догадок.
        var bare = Assert.Single(Mixed, t => t.Time == "02:05.73");
        Assert.Equal(4, bare.Legs.Count);
        Assert.All(bare.Legs, l => Assert.Null(l.SplitTime));
    }

    /// <summary>
    /// У снятой команды на высоте переноса фамилии левее напечатаны причина «DQ / SW» и время
    /// «10.11» — строка-обрывок отбрасывалась целиком, и фамилия из двух слов пропадала:
    /// нога «איריס» без фамилии, импорт заводил карточку-тень (6595, 07.10.2026; тот же дефект
    /// у двух ног 73056 — comp 64 получил тени #62906, #62907).
    /// </summary>
    [Fact]
    public void DisqualifiedTeam_TwoWordSurnames_WrappedBesideReason_AreKept()
    {
        var beitEil = Assert.Single(Parse("loglig-split-relay-11586-58158-women-free.pdf"), t => t.Club == "הפועל בית איל");
        Assert.Equal("DQ", beitEil.Status);
        Assert.Equal(
            [("מירב", "הרוש"), ("נאוה", "טל"), ("איריס", "סברנסקי נקש"), ("תמר", "ערמון אריאלי")],
            beitEil.Legs.Select(l => (l.FirstName, l.LastName)));

        var aquatics = Assert.Single(Women, t => t.Club == "אקוותיקים" && t.Status == "DQ");
        Assert.Contains(aquatics.Legs, l => (l.FirstName, l.LastName) == ("רהאם אריב", "מנסור"));
        Assert.Contains(aquatics.Legs, l => (l.FirstName, l.LastName) == ("ענבר", "קנטרביץ ארליך"));
    }

    [Fact]
    public void MastersBand_ComesFromBandRow()
    {
        // «100-119 מאסטרס שליחים» над первой командой, «160-199» — над следующими.
        Assert.Equal("100-119", Assert.Single(Women, t => t.Time == "03:25.30").Band);
        Assert.Equal("160-199", Assert.Single(Women, t => t.Time == "02:34.67").Band);
        Assert.All(Women, t => Assert.NotNull(t.Band));
    }

    /// <summary>
    /// Путь импорта с 20.09.2026 (Д7): состав и владельца строки склейщик ставит, а ВРЕМЕНА
    /// ЭТАПОВ — нет, их пишет --attach-splits по строкам базы. Два писателя одного поля —
    /// та самая ситуация, из которой выросли дубли 1581.
    /// </summary>
    [Fact]
    public void Enricher_WithoutSplitTimes_FixesOwnerButLeavesLegTimesEmpty()
    {
        var json = new JsonArray(
            Row("02:34.67", 2, 8, (1981, "קיגוסטמלס", "אניה"), (1990, "נוסם", "שירי"), (1981, "ברילר", "דגנית"), (1974, "קמר", "פלר"))
        ).ToJsonString();

        var (outJson, report) = RelaySplitEnricher.Apply(json,
            new[] { new RelaySplitEvent("individual_medley", "4X50", Women) }, writeSplitTimes: false);

        Assert.Equal(1, report.Enriched);
        var rows = JsonNode.Parse(outJson)!.AsArray();
        var legs = rows[0]!["relay_swimmers"]!.AsArray();
        Assert.Equal("גוסטמלסקי", (string?)legs[0]!["last_name"]);
        Assert.Null((string?)legs[0]!["split_time"]);
        Assert.Equal("גוסטמלסקי", (string?)rows[0]!["last_name"]);
    }

    [Fact]
    public void Enricher_MatchesByHeatLaneTime_AndChecksBirthYears()
    {
        var json = new JsonArray(
            Row("02:34.67", 2, 8, (1981, "קיגוסטמלס", "אניה"), (1990, "נוסם", "שירי"), (1981, "ברילר", "דגנית"), (1974, "קמר", "פלר")),
            // Та же дорожка и заплыв, другое время — чужая команда, не трогаем.
            Row("02:30.00", 2, 8, (1981, "x", "y"), (1990, "x", "y"), (1981, "x", "y"), (1974, "x", "y")),
            // Своя команда, но годы ног не сошлись — не трогаем.
            Row("02:24.29", 2, 4, (1990, "a", "b"), (1976, "a", "b"), (1998, "a", "b"), (1999, "a", "b"))
        ).ToJsonString();

        var (outJson, report) = RelaySplitEnricher.Apply(json,
            new[] { new RelaySplitEvent("individual_medley", "4X50", Women) });

        Assert.Equal(1, report.Enriched);
        Assert.Equal(1, report.BirthYearMismatch);

        var rows = JsonNode.Parse(outJson)!.AsArray();
        var legs = rows[0]!["relay_swimmers"]!.AsArray();
        Assert.Equal("גוסטמלסקי", (string?)legs[0]!["last_name"]);
        Assert.Equal("00:30.25", (string?)legs[0]!["split_time"]);
        // Владелец строки — первая нога, уже с целой фамилией.
        Assert.Equal(("גוסטמלסקי", "אניה", 1981),
            ((string?)rows[0]!["last_name"], (string?)rows[0]!["first_name"], (int?)rows[0]!["birth_year"]));
        Assert.Null((string?)rows[1]!["relay_swimmers"]![0]!["split_time"]);
        Assert.Equal("a", (string?)rows[2]!["relay_swimmers"]![0]!["last_name"]);
    }

    private static List<SplitRelayTeam> OpenWater => Parse("loglig-split-relay-14032-75021-openwater-mixed.pdf");

    /// <summary>
    /// Микс-эстафета открытой воды 4X1500 (Эйлат-2026, loglig 14032 / 75021): итоговое время
    /// с ЧАСАМИ — «1:30:08.17». Без часов в шаблоне у всех 17 команд не было времени, и доклейка
    /// состава не находила ни одной — оставались порванные фамилии основного протокола.
    /// </summary>
    [Fact]
    public void OpenWaterRelay_HourTimes_AndCleanNames()
    {
        var teams = OpenWater;
        Assert.Equal(17, teams.Count);
        Assert.All(teams, t => Assert.Equal(4, t.Legs.Count));

        var batYam = Assert.Single(teams, t => t.Time == "1:30:08.17");
        Assert.Equal((1, 4, "הפועל בת ים"), (batYam.Heat, batYam.Lane, batYam.Club));
        // Основной протокол рвёт «פילאר מריה» переносом и даёт «מריהפילאר».
        Assert.Equal(("חשמה", "פילאר מריה"), (batYam.Legs[3].LastName, batYam.Legs[3].FirstName));

        var dq = Assert.Single(teams, t => t.Status == "DQ");
        Assert.Null(dq.Time);
        Assert.Equal("וייסלר ברנד", dq.Legs[3].LastName);
    }

    /// <summary>
    /// Снятая команда сверяется по статусу вместо времени: заплыв + дорожка + «DQ». Иначе её
    /// порванные фамилии («ברנדוייסלר») уезжали в импорт и заводили пловцов-теней.
    /// </summary>
    [Fact]
    public void Enricher_DisqualifiedTeam_MatchedByStatus()
    {
        static JsonObject Dq(string status) => new()
        {
            ["is_relay"] = true,
            ["event_style_name"] = "freestyle",
            ["event_style_len"] = "4X1500",
            ["heat"] = 1,
            ["lane"] = 17,
            ["time"] = "",
            ["time_fail_note"] = status,
            ["relay_swimmers"] = new JsonArray(
                new JsonObject { ["order"] = 1, ["last_name"] = "במירושניקו", ["first_name"] = "ניק", ["birth_year"] = 2010 },
                new JsonObject { ["order"] = 2, ["last_name"] = "אשלגי", ["first_name"] = "אדם", ["birth_year"] = 2010 },
                new JsonObject { ["order"] = 3, ["last_name"] = "פלסיאן", ["first_name"] = "שי", ["birth_year"] = 2012 },
                new JsonObject { ["order"] = 4, ["last_name"] = "ברנדוייסלר", ["first_name"] = "תמר", ["birth_year"] = 2010 }),
        };

        var json = new JsonArray(Dq("DQ")).ToJsonString();
        var (outJson, report) = RelaySplitEnricher.Apply(json,
            new[] { new RelaySplitEvent("freestyle", "4X1500", OpenWater) }, writeSplitTimes: false);

        Assert.Equal(1, report.Enriched);
        var legs = JsonNode.Parse(outJson)!.AsArray()[0]!["relay_swimmers"]!.AsArray();
        Assert.Equal("מירושניקוב", (string?)legs[0]!["last_name"]);
        Assert.Equal("וייסלר ברנד", (string?)legs[3]!["last_name"]);

        // Статус другой — это не та команда.
        var (_, other) = RelaySplitEnricher.Apply(new JsonArray(Dq("NS")).ToJsonString(),
            new[] { new RelaySplitEvent("freestyle", "4X1500", OpenWater) }, writeSplitTimes: false);
        Assert.Equal(0, other.Enriched);
    }

    [Fact]
    public void MastersBuilder_ReplacesRelays_WithBandsGenderAndPlaces()
    {
        // Основной разбор: одна личная строка + одна кривая эстафета (сквозное место, mix на всех).
        var json = new JsonArray(
            new JsonObject { ["competition"] = "Comp", ["date"] = "10/01/2026", ["is_relay"] = false, ["time"] = "00:32.06" },
            new JsonObject
            {
                ["competition"] = "Comp", ["date"] = "10/01/2026", ["is_relay"] = true, ["time"] = "02:34.67",
                ["position"] = 18, ["event_category"] = "mix-120-159",
            }).ToJsonString();

        var events = new[]
        {
            new RelayBuildEvent("individual_medley", "4X50", "female", "4X50 מעורב שליחים", Women),
            new RelayBuildEvent("individual_medley", "4X50", "none", "4X50 מעורב שליחים", Mixed),
        };
        Assert.True(RelayMastersBuilder.CanBuild(events));
        var built = RelayMastersBuilder.Build(json, events)!.Value;
        Assert.Equal(1, built.Replaced);
        Assert.Equal(Women.Count + Mixed.Count, built.Relays);

        var rows = JsonNode.Parse(built.Json)!.AsArray();
        Assert.Equal(1 + built.Relays, rows.Count); // личная строка на месте

        // Женская команда Гостомельской: 3-я в полосе 160-199 (loglig showCategories), пол female.
        var own = rows.Single(r => (string?)r!["time"] == "02:34.67")!;
        Assert.Equal(("female", "160-199", "160-199", "160", 3),
            ((string?)own["event_style_gender"], (string?)own["event_category"], (string?)own["age_group"],
             (string?)own["event_style_age"], (int?)own["position"]));
        Assert.Equal("00:30.25", (string?)own["relay_swimmers"]![0]!["split_time"]);

        // Микст: пол none, категория mix-<полоса>, 1-е место в mix-160-199.
        var mixed = rows.Single(r => (string?)r!["time"] == "01:58.43")!;
        Assert.Equal(("mixed", "mix-160-199", 1), // Э4: смешанная — mixed, не «неизвестен»
            ((string?)mixed["event_style_gender"], (string?)mixed["event_category"], (int?)mixed["position"]));

        // DQ — без места, статус в time_fail_note.
        var dqs = rows.Where(r => (string?)r!["time_fail_note"] == "DQ").ToList();
        Assert.NotEmpty(dqs);
        Assert.All(dqs, dq =>
        {
            Assert.Null((int?)dq!["position"]);
            Assert.True((bool?)dq["time_fail"]);
        });
    }

    [Fact]
    public void MastersBuilder_RefusesWithoutBands()
    {
        var noBand = Women.Select(t => t with { Band = null }).ToList();
        var events = new[] { new RelayBuildEvent("individual_medley", "4X50", "female", "x", noBand) };
        Assert.False(RelayMastersBuilder.CanBuild(events));
        Assert.Null(RelayMastersBuilder.Build("[]", events));
    }

    // Маккабия мастерс 2026 (loglig 15155): мужские 4×50 комплекс и вольным. Здесь сборка
    // отказывала целиком (22.09.2026): время «02:17.0» не читалось, и у двух команд источник
    // не печатает ни одной ноги.
    private static List<SplitRelayTeam> MaccabiahMenMedley => Parse("loglig-split-relay-15155-82535-men-medley.pdf");
    private static List<SplitRelayTeam> MaccabiahMenFree => Parse("loglig-split-relay-15155-82534-men-free.pdf");

    [Fact]
    public void Time_WithOneFractionDigit_IsReadAndNotGluedToClub()
    {
        var team = Assert.Single(MaccabiahMenMedley, t => t.Time == "02:17.0");
        Assert.Equal("הפועל בת ים", team.Club);
        Assert.Equal("160-199", team.Band);
        Assert.Equal(4, team.Legs.Count);
    }

    [Fact]
    public void MastersBuilder_BorrowsLegsFromProtocol_ForTeamWithoutLegsInSource()
    {
        var events = new[] { new RelayBuildEvent("individual_medley", "4X50", "male", "4X50 מעורב שליחים", MaccabiahMenMedley) };
        Assert.False(RelayMastersBuilder.CanBuild(events)); // «מכבי עולם המים» 01:52.53 — без ног

        var protocol = Row("01:52.53", 6, 1, (1980, "הר-שי", "לירון"), (2000, "ישראלי", "עידו"), (2004, "אריאל", "אסף"));
        protocol["club"] = "מכבי עולם המים";
        var built = RelayMastersBuilder.Build(new JsonArray(protocol).ToJsonString(), events)!.Value;

        Assert.Equal(MaccabiahMenMedley.Count, built.Relays);
        var row = JsonNode.Parse(built.Json)!.AsArray().Single(r => (string?)r!["time"] == "01:52.53")!;
        Assert.Equal(("male", "הר-שי", 3), ((string?)row["event_style_gender"], (string?)row["last_name"],
            row["relay_swimmers"]!.AsArray().Count));
    }

    [Fact]
    public void MastersBuilder_RefusesWhenLeglessTeamIsNotInProtocol()
    {
        var events = new[] { new RelayBuildEvent("individual_medley", "4X50", "male", "x", MaccabiahMenMedley) };
        Assert.Null(RelayMastersBuilder.Build("[]", events));
    }

    [Fact]
    public void MastersBuilder_DropsLeglessNoShowTeam()
    {
        // «MIX Maccabiah» — NS и ни одной ноги: заплыва не было, строить нечего.
        var ns = Assert.Single(MaccabiahMenFree, t => t.Legs.Count == 0);
        Assert.Equal("NS", ns.Status);

        var events = new[] { new RelayBuildEvent("freestyle", "4X50", "male", "4X50 חופשי שליחים", MaccabiahMenFree) };
        var personal = new JsonArray(new JsonObject { ["is_relay"] = false, ["time"] = "00:32.06" }).ToJsonString();
        var built = RelayMastersBuilder.Build(personal, events)!.Value;
        Assert.Equal(MaccabiahMenFree.Count - 1, built.Relays);
    }

    [Theory]
    [InlineData("מאסטרס נ 21-99", "female")]
    [InlineData("מאסטרס ג 21-99", "male")]
    [InlineData("מיקס מיקס 21-99", "mixed")] // Э4
    [InlineData("בנות 11-12", "female")]
    public void RelayGender_ReadsGenderWordAnywhereInCategory(string category, string expected)
    {
        // Сетка loglig берёт пол из первого слова и у мастерсов («מאסטרס …») отдаёт none.
        Assert.Equal(expected, Swimm.Parsing.Discovery.LogligRelaySplitProvider.RelayGender(category, "none"));
    }

    private static JsonObject Row(string time, int heat, int lane, params (int Year, string Last, string First)[] legs) => new()
    {
        ["is_relay"] = true,
        ["event_style_name"] = "individual_medley",
        ["event_style_len"] = "4X50",
        ["heat"] = heat,
        ["lane"] = lane,
        ["time"] = time,
        ["relay_swimmers"] = new JsonArray(legs.Select((l, i) => (JsonNode)new JsonObject
        {
            ["order"] = i + 1,
            ["last_name"] = l.Last,
            ["first_name"] = l.First,
            ["birth_year"] = l.Year,
            ["club"] = null,
            ["split_time"] = null,
        }).ToArray()),
    };
}
