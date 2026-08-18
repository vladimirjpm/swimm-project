namespace Swimm.Application.Mapping;

/// <summary>
/// Один заплыв, поданный в клубный зачёт. <paramref name="Points"/> уже посчитаны вызывающим
/// (правило очков живёт в Infrastructure) — здесь только агрегация и ранжирование.
/// </summary>
/// <param name="ClubKey">Имя клуба по правилу «club → relay_team_name → club_en» — исторический
/// ключ зачёта. Нужен и при группировке по Id: он идёт в витрину как подпись.</param>
/// <param name="Place">Место, по которому идёт зачёт: протокольное либо объединённое
/// (Combine All Results) — решает вызывающий.</param>
public sealed record ClubScoringRow(
    int ClubId,
    string ClubKey,
    int SwimmerId,
    int? Place,
    bool IsRelay,
    int Points);

/// <summary>Строка клубного зачёта: место клуба и его слагаемые.</summary>
public sealed record ClubStandingRow(
    int ClubId,
    string ClubKey,
    int Rank,
    int Points,
    int SwimmerCount,
    int ScoringSwims,
    int SwimCount,
    int Gold,
    int Silver,
    int Bronze);

/// <summary>Чем ключуется зачёт.</summary>
public enum ClubStandingKey
{
    /// <summary>По имени клуба — исторический ключ <c>/api/club-summary</c> и карточки Top clubs.</summary>
    ByName,

    /// <summary>По <c>Club.Id</c> — для страницы клуба и материализованной таблицы зачёта.
    /// Отличается от <see cref="ByName"/> только там, где два разных клуба носят одно имя
    /// (следы дублей): по имени они схлопнутся в одну строку, по Id — останутся разными.</summary>
    ById
}

/// <summary>
/// Клубный зачёт: заплывы → таблица клубов с местами. Единственный алгоритм на два
/// применения — витрину соревнования (<c>/api/club-summary</c>, карточка Top clubs) и
/// материализованный зачёт страницы клуба. Вторая копия неизбежно разъехалась бы с первой,
/// а расхождение в очках заметить почти невозможно.
///
/// Что делает вызывающий ДО этого класса (здесь не проверяется):
/// <list type="bullet">
/// <item>отсеивает псевдоклубы (<c>Club.IsPseudo</c> — страны/сборные в графе клуба);</item>
/// <item>считает <c>Points</c> по правилу соревнования, включая эстафетный множитель;</item>
/// <item>выбирает <c>Place</c>: протокольное или объединённое место (Combine All Results).</item>
/// </list>
/// </summary>
public static class ClubStandingCalculator
{
    /// <summary>
    /// Таблица зачёта: от лучшего к худшему, с проставленными местами.
    /// Ранжирование обычное спортивное: равные очки → равное место, следующее место
    /// пропускается (1, 2, 2, 4). Порядок при равенстве — по ключу клуба, чтобы выдача
    /// была стабильной между запросами.
    /// </summary>
    public static List<ClubStandingRow> Build(
        IEnumerable<ClubScoringRow> rows, ClubStandingKey key = ClubStandingKey.ByName)
    {
        var acc = new Dictionary<object, Bucket>();

        foreach (var r in rows)
        {
            object k = key == ClubStandingKey.ById ? r.ClubId : r.ClubKey;
            if (!acc.TryGetValue(k, out var b))
                acc[k] = b = new Bucket(r.ClubId, r.ClubKey);

            b.Points += r.Points;
            b.SwimCount += 1;
            // ScoringSwims — заплывы, ПРИНЁСШИЕ очки (попали в шкалу правила), а не «доплывшие».
            // Подпись в карточке — «scoring swims»; Combine All Results режет их вдвое.
            if (r.Points > 0) b.ScoringSwims += 1;
            // Пловцы — по SwimmerId, а не по фамилии: однофамильцы схлопывались в одного.
            // Владелец эстафетной строки тоже пловец клуба (полный состав — в RelayMembers).
            b.Swimmers.Add(r.SwimmerId);
            switch (r.Place)
            {
                case 1: b.Gold += 1; break;
                case 2: b.Silver += 1; break;
                case 3: b.Bronze += 1; break;
            }
        }

        // Тай-брейк по имени — компаратором по умолчанию: ровно так сортировался
        // /api/club-summary до выноса расчёта сюда, менять порядок выдачи заодно не хочется.
        var ordered = acc.Values
            .OrderByDescending(b => b.Points)
            .ThenBy(b => b.ClubKey)
            .ToList();

        var result = new List<ClubStandingRow>(ordered.Count);
        var rank = 0;
        var seen = 0;
        int? prevPoints = null;
        foreach (var b in ordered)
        {
            seen += 1;
            if (prevPoints != b.Points) { rank = seen; prevPoints = b.Points; }
            result.Add(new ClubStandingRow(
                ClubId: b.ClubId,
                ClubKey: b.ClubKey,
                Rank: rank,
                Points: b.Points,
                SwimmerCount: b.Swimmers.Count,
                ScoringSwims: b.ScoringSwims,
                SwimCount: b.SwimCount,
                Gold: b.Gold,
                Silver: b.Silver,
                Bronze: b.Bronze));
        }
        return result;
    }

    private sealed class Bucket(int clubId, string clubKey)
    {
        public int ClubId { get; } = clubId;
        public string ClubKey { get; } = clubKey;
        public HashSet<int> Swimmers { get; } = [];
        public int Points;
        public int ScoringSwims;
        public int SwimCount;
        public int Gold;
        public int Silver;
        public int Bronze;
    }
}
