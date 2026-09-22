using Swimm.Application.Mapping;
using Xunit;
using Record = Swimm.Domain.Entities.Record;
using Swimm.Domain.Entities;

namespace Swimm.Tests;

/// <summary>Тесты чистого детектора «новых рекордов» соревнования (образец — PointRulesClubsScoringTests).</summary>
public class CompetitionRecordsDetectorTests
{
    private static Record Rec(string category, string ageKey, string time,
        string style = "backstroke", string distance = "50m", string gender = "male", string pool = "50m") => new()
    {
        RegionType = "country", RegionCode = "ISR",
        Category = category, AgeKey = ageKey,
        Gender = gender, PoolType = pool, Style = style, Distance = distance,
        Time = time, HolderName = "Holder"
    };

    private static RecordCandidateRow Row(int timeMs, int? birthYear = 2016, string time = "00:40.00",
        string style = "backstroke", string distance = "50", string gender = "male", string pool = "50m",
        bool isMasters = false, long resultId = 1, string ageGroup = "") => new(
        resultId, 100, "First", "Last", "Club", style, distance, gender, pool,
        birthYear, new DateTime(2026, 7, 15, 0, 0, 0, DateTimeKind.Utc), timeMs, time, 1, isMasters,
        ageGroup);

    [Fact]
    public void BeatsAgeRecord_ByComputedAge()
    {
        // 2026 - 2016 = возраст 10; рекорд Age 10 = 45.00, заплыв 40.00 — побит.
        var result = CompetitionRecordsDetector.Detect(
            [Rec("age", "10", "45.00")], [Row(40_000)]);

        var dto = Assert.Single(result);
        Assert.Equal("Age 10 record", dto.Kind);
        Assert.Equal(1, dto.ResultId);
    }

    /// <summary>
    /// Реальный случай, на котором ось всплыла: מיה גרינברג, 2015 г.р., 50 брассом 39.02 на
    /// старте 31/10/2025. По календарю (ось федерации) ей 10, по сезону 2025/26 — 11, и
    /// сверка попадает в РАЗНЫЕ ступени справочника. Осень — единственное место, где оси
    /// расходятся, поэтому тесты держат именно эту дату.
    /// </summary>
    private static RecordCandidateRow AutumnMaya() =>
        Row(39_020, birthYear: 2015, time: "00:39.02", style: "breaststroke",
            gender: "female", pool: "25m") with { CompetitionDate = new DateTime(2025, 10, 31) };

    private static Record[] MayaSteps() =>
    [
        Rec("age", "10", "39.85", style: "breaststroke", gender: "female", pool: "25m"),
        Rec("age", "11", "37.38", style: "breaststroke", gender: "female", pool: "25m")
    ];

    [Fact]
    public void AutumnSwim_CalendarAxis_HitsFederationStep()
    {
        // Ось по умолчанию — календарная, как ведёт справочник федерация: ступень Age 10,
        // 39.02 быстрее 39.85 → рекорд.
        var dto = Assert.Single(CompetitionRecordsDetector.Detect(MayaSteps(), [AutumnMaya()]));
        Assert.Equal("Age 10 record", dto.Kind);
    }

    [Fact]
    public void AutumnSwim_SeasonAxis_HitsOurStep()
    {
        // Сезонная ось (наш возраст в сезоне): ступень Age 11, её рекорд 37.38 быстрее —
        // рекорда нет. Ступень Age 10 при этом НЕ проверяется вовсе, она уже чужая.
        Assert.Empty(CompetitionRecordsDetector.Detect(
            MayaSteps(), [AutumnMaya()], RecordAgeAxis.Season));

        var slowerEleven = new[] { Rec("age", "11", "39.50", style: "breaststroke", gender: "female", pool: "25m") };
        var dto = Assert.Single(CompetitionRecordsDetector.Detect(
            slowerEleven, [AutumnMaya()], RecordAgeAxis.Season));
        Assert.Equal("Age 11 record", dto.Kind);
    }

    [Fact]
    public void SummerSwim_BothAxes_Agree()
    {
        // С января по август год окончания сезона совпадает с календарным, поэтому ось
        // ничего не меняет — и настройку в это время можно крутить безнаказанно.
        var records = new[] { Rec("age", "10", "45.00") };

        Assert.Single(CompetitionRecordsDetector.Detect(records, [Row(40_000)], RecordAgeAxis.Calendar));
        Assert.Single(CompetitionRecordsDetector.Detect(records, [Row(40_000)], RecordAgeAxis.Season));
    }

    [Theory]
    [InlineData("calendar", RecordAgeAxis.Calendar)]
    [InlineData("season", RecordAgeAxis.Season)]
    [InlineData("Season", RecordAgeAxis.Season)]
    [InlineData(" season ", RecordAgeAxis.Season)]
    [InlineData("", RecordAgeAxis.Calendar)]
    [InlineData("сезонная", RecordAgeAxis.Calendar)]
    [InlineData(null, RecordAgeAxis.Calendar)]
    public void AxisSetting_ParsesValue_UnknownFallsBackToCalendar(string? raw, RecordAgeAxis expected)
        => Assert.Equal(expected, RecordAgeAxisSetting.Parse(raw));

    [Fact]
    public void AxisSetting_ReadsFromSettingsService_DefaultsToCalendar()
    {
        Assert.Equal(RecordAgeAxis.Calendar, RecordAgeAxisSetting.From(new SettingsStub()));
        Assert.Equal(RecordAgeAxis.Season, RecordAgeAxisSetting.From(
            new SettingsStub(new() { ["RecordAgeAxis"] = "season" })));

        // Настройки нет вовсе (изолированный вызов) — тоже ось федерации.
        Assert.Equal(RecordAgeAxis.Calendar, RecordAgeAxisSetting.From(null));
    }

    private sealed class SettingsStub : Swimm.Application.Abstractions.ISettingsService
    {
        private readonly Dictionary<string, string> _values;
        public SettingsStub(Dictionary<string, string>? values = null) => _values = values ?? new();
        public IReadOnlyList<Swimm.Application.Dtos.AdminSetting> GetAll() => [];
        public Swimm.Application.Dtos.AdminSetting? Get(string key) => null;
        public T GetValue<T>(string key, T fallback) =>
            _values.TryGetValue(key, out var raw) ? (T)Convert.ChangeType(raw, typeof(T)) : fallback;
        public bool Update(string key, string newValue) { _values[key] = newValue; return true; }
    }

    [Fact]
    public void EqualTime_CountsAsRecord()
    {
        // Семантика клиента isRecordTime: время ≤ рекорда — рекорд.
        var result = CompetitionRecordsDetector.Detect(
            [Rec("age", "10", "40.00")], [Row(40_000)]);
        Assert.Single(result);
    }

    [Fact]
    public void SlowerTime_NoRecord()
    {
        var result = CompetitionRecordsDetector.Detect(
            [Rec("age", "10", "39.00")], [Row(40_000)]);
        Assert.Empty(result);
    }

    [Fact]
    public void AxisMismatch_PoolStyleDistanceGender_NoRecord()
    {
        var records = new[]
        {
            Rec("age", "10", "45.00", pool: "25m"),
            Rec("age", "10", "45.00", style: "freestyle"),
            Rec("age", "10", "45.00", distance: "100m"),
            Rec("age", "10", "45.00", gender: "female"),
        };
        Assert.Empty(CompetitionRecordsDetector.Detect(records, [Row(40_000)]));
    }

    [Fact]
    public void OpenKey_MatchedRegardlessOfAge()
    {
        // open (AgeKey="") проверяется независимо от возраста — там и живёт национальный рекорд.
        var records = new[] { Rec("open", "", "41.00") };
        var result = CompetitionRecordsDetector.Detect(records, [Row(40_000, birthYear: null)]);

        var dto = Assert.Single(result);
        Assert.Equal("Open record", dto.Kind);
    }

    [Fact]
    public void LegacyNationalKey_Ignored()
    {
        // Ось ("age","ISR") — набор legacy-RecordsSeeder, который импортом не обновлялся и
        // протух вместе с ошибками старого парсера: заплыв 41.93 на 50 спине «бил» там
        // национальный 53.60 (время стометровки, заехавшее в полтинник). Ось убрана —
        // такие записи, даже если сид их воскресит, больше не дают рекордов.
        var records = new[] { Rec("age", "ISR", "42.00") };
        Assert.Empty(CompetitionRecordsDetector.Detect(records, [Row(40_000, birthYear: null)]));
    }

    [Fact]
    public void Masters_UsesRangeAgeKey()
    {
        // 2026 - 1998 = 28 → диапазон 25-29; обычный age-ключ не проверяется.
        var records = new[] { Rec("masters", "25-29", "45.00"), Rec("age", "28", "45.00") };
        var result = CompetitionRecordsDetector.Detect(
            records, [Row(40_000, birthYear: 1998, isMasters: true)]);

        var dto = Assert.Single(result);
        Assert.Equal("Masters 25-29", dto.Kind);
    }

    [Fact]
    public void FastestSwimPerAxis_Wins()
    {
        // Два заплыва бьют один рекорд — карточка одна, у более быстрого.
        var rows = new[]
        {
            Row(40_000, resultId: 1),
            Row(39_000, resultId: 2, time: "00:39.00"),
        };
        var result = CompetitionRecordsDetector.Detect([Rec("age", "10", "45.00")], rows);

        var dto = Assert.Single(result);
        Assert.Equal(2, dto.ResultId);
        Assert.Equal("00:39.00", dto.Time);
    }

    [Theory]
    [InlineData("35.64", 35_640)]
    [InlineData("02:45.46", 165_460)]
    [InlineData("1:02:45.46", 3_765_460)]
    public void ParseTimeToMs_Formats(string time, int expectedMs)
        => Assert.Equal(expectedMs, CompetitionRecordsDetector.ParseTimeToMs(time));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("1:2:3:4")]
    public void ParseTimeToMs_Garbage_ReturnsNull(string? time)
        => Assert.Null(CompetitionRecordsDetector.ParseTimeToMs(time));

    // ── Э4б: эстафеты (records-relays-plan, 22.09.2026) ──────────────────────────────

    private static RecordCandidateRow Relay(int timeMs, string gender, params int[] birthYears) =>
        Row(timeMs, birthYear: 2012, time: "01:50.00", style: "freestyle", distance: "4X50",
            gender: gender, pool: "25m") with
        {
            IsRelay = true, MemberBirthYears = birthYears, TeamName = "Maccabi Haifa A",
        };

    /// <summary>
    /// Возрастная ступень эстафеты — по САМОМУ СТАРШЕМУ участнику (2026 − 2011 = 15), а не по
    /// владельцу строки (первая нога, 2012 → 14). Держатель в карточке — команда.
    /// </summary>
    [Fact]
    public void Relay_AgeStepByOldestMember_HolderIsTeam()
    {
        var records = new[]
        {
            Rec("age", "14", "01:40.00", style: "freestyle", distance: "4X50m", gender: "mixed", pool: "25m"),
            Rec("age", "15", "01:55.00", style: "freestyle", distance: "4X50m", gender: "mixed", pool: "25m"),
        };

        var hit = Assert.Single(CompetitionRecordsDetector.Detect(records, [Relay(110_000, "mixed", 2012, 2012, 2011, 2012)]));

        Assert.Equal("Age 15 record", hit.Kind);
        Assert.Equal("Maccabi Haifa A", hit.HolderName);
    }

    /// <summary>Смешанная сверяется со смешанным рекордом, не с мужским.</summary>
    [Fact]
    public void Relay_MixedMatchesMixedOnly()
    {
        var records = new[]
        {
            Rec("open", "", "01:55.00", style: "freestyle", distance: "4X50m", gender: "male", pool: "25m"),
        };
        Assert.Empty(CompetitionRecordsDetector.Detect(records, [Relay(110_000, "mixed", 2000, 2000, 2000, 2000)]));
    }

    /// <summary>«none» — пол неизвестен: никакой метки, даже если время быстрее всего.</summary>
    [Fact]
    public void Relay_UnknownGender_NoRecord()
    {
        var records = new[]
        {
            Rec("open", "", "01:55.00", style: "freestyle", distance: "4X50m", gender: "male", pool: "25m"),
            Rec("open", "", "01:55.00", style: "freestyle", distance: "4X50m", gender: "mixed", pool: "25m"),
        };
        Assert.Empty(CompetitionRecordsDetector.Detect(records, [Relay(100_000, "none", 2000, 2000, 2000, 2000)]));
    }

    /// <summary>Год рождения хоть одной ноги неизвестен — возрастную ступень не угадываем, только open.</summary>
    [Fact]
    public void Relay_IncompleteRoster_OpenOnly()
    {
        var records = new[]
        {
            Rec("age", "14", "01:55.00", style: "freestyle", distance: "4X50m", gender: "female", pool: "25m"),
            Rec("open", "", "01:45.00", style: "freestyle", distance: "4X50m", gender: "female", pool: "25m"),
        };
        Assert.Empty(CompetitionRecordsDetector.Detect(records, [Relay(110_000, "female", 2012, 2012, 0, 2012)]));

        var open = Assert.Single(CompetitionRecordsDetector.Detect(records, [Relay(104_000, "female", 2012, 2012, 0, 2012)]));
        Assert.Equal("Open record", open.Kind);
    }
}
