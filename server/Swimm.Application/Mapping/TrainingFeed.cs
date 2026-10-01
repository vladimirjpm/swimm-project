using System.Globalization;
using Swimm.Application.Dtos;

namespace Swimm.Application.Mapping;

/// <summary>
/// Лента тренировок группы (docs/plans/trainings-tab-redesign-plan.md, Т1а) — чистые функции над
/// повторами одной сессии, без БД: показатели карточки, строка «You», сеты для «Sets & times».
/// Повторы читает только <c>IHubGroupTrainingRepository</c> (И16), здесь — только счёт.
/// </summary>
public static class TrainingFeed
{
    /// <summary>
    /// Один записанный повтор. <paramref name="Name"/> / <paramref name="NameEn"/> — «Фамилия Имя»
    /// на иврите и латиницей (на витрине иврит по умолчанию). <paramref name="TimeMs"/> null или 0 —
    /// времени нет (строка источника без замера).
    /// </summary>
    public sealed record Repeat(
        int SessionId, int SetNo, int OrderNo, int SwimmerId, string Name, string NameEn,
        string Distance, string Style, string Time, int? TimeMs, string? Intensity);

    /// <summary>Уровень пловца в группе — цвет и подпись строки «Sets & times».</summary>
    public sealed record Level(int Id, int Rank, string Name, string? Color);

    /// <summary>
    /// Показатели карточки (Р2, Р3): объём ОДНОГО пловца — сумма дистанций уникальных повторов
    /// (<c>SetNo</c> + <c>OrderNo</c>) с временем, и сколько пловцов записано. Повтор плыли разные
    /// дорожки разной длины — берём наибольшую (объём тренировки, а не слабейшей дорожки).
    /// Времён нет — null (карточка без показателей). «Avg effort» убран (Р3).
    /// </summary>
    public static TrainingSessionStatsDto? Stats(IReadOnlyCollection<Repeat> repeats)
    {
        var timed = repeats.Where(HasTime).ToList();
        if (timed.Count == 0) return null;

        var meters = timed
            .GroupBy(r => (r.SetNo, r.OrderNo))
            .Sum(g => g.Max(r => Meters(r.Distance)));
        return new TrainingSessionStatsDto
        {
            Meters = meters,
            Swimmers = timed.Select(r => r.SwimmerId).Distinct().Count(),
        };
    }

    /// <summary>
    /// Строка «You» (Р4): ключевой сет помечен тренером ★ — быстрейший повтор в нём первого из
    /// «моих» пловцов (<paramref name="mine"/> — уже в порядке «me», затем семья), у кого в этом сете
    /// есть время. Сет не помечен или моих повторов в нём нет — null: никакой эвристики «лучшего».
    /// </summary>
    public static TrainingSessionMyKeyDto? MyKey(IReadOnlyCollection<Repeat> repeats, int? keySetNo,
        IReadOnlyList<(int SwimmerId, string Kind)> mine)
    {
        if (keySetNo is not int setNo || mine.Count == 0) return null;

        var inSet = repeats.Where(r => r.SetNo == setNo && HasTime(r)).ToList();
        foreach (var (swimmerId, kind) in mine)
        {
            var best = inSet
                .Where(r => r.SwimmerId == swimmerId)
                .OrderBy(r => r.TimeMs).ThenBy(r => r.OrderNo)
                .FirstOrDefault();
            if (best == null) continue;

            return new TrainingSessionMyKeyDto
            {
                SetNo = setNo,
                OrderNo = best.OrderNo,
                SwimmerId = swimmerId,
                Kind = kind,
                Name = best.Name,
                NameEn = best.NameEn,
                Distance = best.Distance,
                Style = best.Style,
                Time = best.Time,
            };
        }
        return null;
    }

    /// <summary>
    /// Сеты сессии для «Sets & times»: по возрастанию <c>SetNo</c>, внутри — повтор, затем время,
    /// затем пловец (порядок источника). Строки без времени показываем тоже — источник их записал
    /// (время пустое). «Моя» строка — пловец из круга <paramref name="mine"/>.
    /// </summary>
    public static List<TrainingSetDto> Sets(IReadOnlyCollection<Repeat> repeats, int? keySetNo,
        IReadOnlySet<int> mine, IReadOnlyDictionary<int, Level> levelBySwimmer) =>
        repeats
            .GroupBy(r => r.SetNo)
            .OrderBy(g => g.Key)
            .Select(g => new TrainingSetDto
            {
                SetNo = g.Key,
                IsKey = g.Key == keySetNo,
                Rows = g
                    .OrderBy(r => r.OrderNo)
                    .ThenBy(r => HasTime(r) ? 0 : 1).ThenBy(r => r.TimeMs)
                    .ThenBy(r => r.SwimmerId)
                    .Select(r => new TrainingSetRowDto
                    {
                        OrderNo = r.OrderNo,
                        SwimmerId = r.SwimmerId,
                        Name = r.Name,
                        NameEn = r.NameEn,
                        Distance = r.Distance,
                        Style = r.Style,
                        Time = HasTime(r) ? r.Time : null,
                        Intensity = string.IsNullOrWhiteSpace(r.Intensity) ? null : r.Intensity.Trim(),
                        Level = levelBySwimmer.TryGetValue(r.SwimmerId, out var l)
                            ? new LanePlanLevelDto { Id = l.Id, Rank = l.Rank, Name = l.Name, Color = l.Color }
                            : null,
                        Mine = mine.Contains(r.SwimmerId),
                    })
                    .ToList(),
            })
            .ToList();

    private static bool HasTime(Repeat r) => r.TimeMs is > 0;

    /// <summary>Дистанция источника — «100», бывает «100m»; не число — 0 (в объём не идёт).</summary>
    public static int Meters(string? distance)
    {
        var digits = new string((distance ?? "").Trim().TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var m) ? m : 0;
    }
}
