using Swimm.Application.Mapping;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Разнос претензии по кумулятивной лестнице рекордов (RQ-1).
///
/// Лестница федерации кумулятивна: одно достижение переносится вверх по возрастам, пока его
/// не побьют. Претензия заводится на ОДНУ ступень — где рекорд реально установлен. Живой
/// случай: RQ-1 заведена на ступень 10, а карточка клуба показывает ступень 11 — и была без
/// значка, хотя это тот же самый спорный заплыв.
/// </summary>
public class RecordIssueSpreaderTests
{
    private static RecordAxes Row(int index, string ageKey, string time = "34.08",
        string holder = "מירה מירוסלבה אושקובה", string date = "20/07/2025") =>
        new(index, "country", "ISR", "age", ageKey, "female", "50m", "backstroke", "50m",
            time, holder, date);

    private static Dictionary<string, string> Issue(string ageKey, string time = "34.08") => new()
    {
        [RecordIssueKey.Of("country", "ISR", "age", ageKey, "female", "50m", "backstroke", "50m", time)]
            = "lcm-faster-than-scm"
    };

    [Fact]
    public void Issue_OnOneStep_MarksTheWholeLadder()
    {
        var records = new[] { Row(0, "10"), Row(1, "11") };

        var reasons = RecordIssueSpreader.Resolve(records, Issue("10"));

        Assert.Equal(2, reasons.Count);
        Assert.Equal("lcm-faster-than-scm", reasons[1]);
    }

    [Fact]
    public void SameTime_DifferentHolder_NotMarked()
    {
        // Совпало время, но достижение чужое — метку тащить нельзя.
        var records = new[] { Row(0, "10"), Row(1, "11", holder: "אחרת שחיינית") };

        var reasons = RecordIssueSpreader.Resolve(records, Issue("10"));

        Assert.Single(reasons);
        Assert.True(reasons.ContainsKey(0));
    }

    [Fact]
    public void SameHolder_DifferentDate_NotMarked()
    {
        // Тот же пловец, то же время, но другой заплыв — это отдельное достижение.
        var records = new[] { Row(0, "10"), Row(1, "11", date: "12/03/2026") };

        var reasons = RecordIssueSpreader.Resolve(records, Issue("10"));

        Assert.Single(reasons);
    }

    [Fact]
    public void RecordBeaten_TimeChanged_NoMarkAtAll()
    {
        // Рекорд побили: время в справочнике сменилось, и старая претензия больше не
        // относится к текущей записи — иначе метка «спорно» висела бы на честном достижении.
        var records = new[] { Row(0, "10", time: "33.10"), Row(1, "11", time: "33.10") };

        Assert.Empty(RecordIssueSpreader.Resolve(records, Issue("10")));
    }

    [Fact]
    public void NoIssues_NothingMarked() =>
        Assert.Empty(RecordIssueSpreader.Resolve(new[] { Row(0, "10") }, new Dictionary<string, string>()));
}
