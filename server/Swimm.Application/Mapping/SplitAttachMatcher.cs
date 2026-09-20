namespace Swimm.Application.Mapping;

/// <summary>Нога команды из источника loglig.</summary>
public sealed record SplitSourceLeg(int Order, int? BirthYear, string? SplitTime);

/// <summary>Команда эстафеты из источника: заплыв, дорожка, итог и четыре ноги.</summary>
public sealed record SplitSourceTeam(
    string Style, string Distance, int Heat, int Lane, string? Time, IReadOnlyList<SplitSourceLeg> Legs);

/// <summary>Личный заплыв из источника: итог, год рождения и отрезки («31.52», «34.84»).</summary>
public sealed record SplitSourceSwim(
    string Style, string Distance, string Gender, string Time, int BirthYear, IReadOnlyList<string> Laps);

/// <summary>Нога эстафеты, уже лежащая в базе (<c>RelayMembers</c>).</summary>
public sealed record RelayLegRow(int MemberId, int LegOrder, int BirthYear, string? SplitTime);

/// <summary>Строка эстафеты из базы — кандидат на доклейку.</summary>
public sealed record RelayRow(
    long ResultId, int RelayId, string Style, string Distance, int? Heat, int? Lane, string? Time,
    IReadOnlyList<RelayLegRow> Legs);

/// <summary>Строка личного заплыва из базы — кандидат на доклейку.</summary>
public sealed record SwimRow(
    long ResultId, string Style, string Distance, string Gender, string? Time, int BirthYear, string? TimeSplit);

/// <summary>Что записать: нога <paramref name="MemberId"/> получает время этапа.</summary>
public sealed record LegSplitWrite(int MemberId, string SplitTime);

/// <summary>Что записать: строка <paramref name="ResultId"/> получает отрезки («31.52;34.84»).</summary>
public sealed record SwimSplitWrite(long ResultId, string TimeSplit);

/// <summary>
/// Итог доклейки. Без «Сверки с файлом» (её делал импорт) это ЕДИНСТВЕННЫЙ контроль — счётчики
/// обязаны объяснять каждую команду и каждого пловца источника.
/// </summary>
public sealed record SplitAttachReport(
    int Teams, int TeamsAttached, int TeamsNoLegsInSource, int TeamsNoLegsInDb,
    int TeamsUnmatched, int TeamsAmbiguous, int TeamsBirthYearMismatch,
    int Swims, int SwimsAttached, int SwimsUnmatched, int SwimsAmbiguous)
{
    /// <summary>Сколько ног и строк реально изменится (у остальных значение уже стоит).</summary>
    public int LegWrites { get; init; }

    public int SwimWrites { get; init; }

    public override string ToString() =>
        $"эстафеты: команд {Teams}, доклеено {TeamsAttached} (ног к записи {LegWrites})"
        + (TeamsNoLegsInSource > 0 ? $", без ног в источнике {TeamsNoLegsInSource}" : "")
        + (TeamsNoLegsInDb > 0 ? $", без ног в базе {TeamsNoLegsInDb}" : "")
        + (TeamsUnmatched > 0 ? $", не нашлось в базе {TeamsUnmatched}" : "")
        + (TeamsAmbiguous > 0 ? $", неоднозначно {TeamsAmbiguous}" : "")
        + (TeamsBirthYearMismatch > 0 ? $", состав не сошёлся по годам {TeamsBirthYearMismatch}" : "")
        + $"; личные: пловцов {Swims}, доклеено {SwimsAttached} (строк к записи {SwimWrites})"
        + (SwimsUnmatched > 0 ? $", не нашлось в базе {SwimsUnmatched}" : "")
        + (SwimsAmbiguous > 0 ? $", неоднозначно {SwimsAmbiguous}" : "");
}

/// <summary>Что писать и что об этом рассказать.</summary>
public sealed record SplitAttachPlan(
    IReadOnlyList<LegSplitWrite> Legs, IReadOnlyList<SwimSplitWrite> Swims, SplitAttachReport Report);

/// <summary>
/// Доклейка промежуточных К СТРОКАМ БАЗЫ, без переимпорта
/// (docs/plans/splits-attach-without-repull-plan.md, решение Влада 20.09.2026).
///
/// Отличие от <c>RelaySplitEnricher</c> / <c>IndividualSplitEnricher</c> — только в том, ЧТО
/// сопоставляем: там разобранный JSON протокола, здесь строки, которые уже в базе. Ключи
/// намеренно ТЕ ЖЕ, иначе в продукте появится второй набор правил:
///   эстафета — стиль + дистанция + заплыв + дорожка + итог, затем состав по годам рождения;
///   личный   — стиль + дистанция + пол + итог + год рождения.
/// Совпадение обязано быть однозначным: 0 и 2+ одинаково значат «не пишем» и идут в отчёт
/// разными счётчиками — молчаливая запись «примерно туда» здесь хуже, чем её отсутствие.
///
/// ⚠ Сумму отрезков с итогом сверяет РАЗБОРЩИК (<c>LogligIndividualSplitParser</c>, ±0.02):
/// пловец с несошедшейся суммой сюда просто не доезжает. Второй такой проверки тут нет
/// намеренно — она разъехалась бы с первой.
///
/// Функция чистая: ни EF, ни сети. Ничего не удаляет и не создаёт — только «кому какое
/// время записать»; идемпотентна (значение уже стоит и совпало — в план записи не попадает).
/// </summary>
public static class SplitAttachMatcher
{
    public static SplitAttachPlan Build(
        IReadOnlyList<RelayRow> relayRows,
        IReadOnlyList<SwimRow> swimRows,
        IReadOnlyList<SplitSourceTeam> teams,
        IReadOnlyList<SplitSourceSwim> swims)
    {
        var legWrites = new List<LegSplitWrite>();
        var swimWrites = new List<SwimSplitWrite>();
        int attached = 0, noLegsSource = 0, noLegsDb = 0, unmatched = 0, ambiguous = 0, yearMismatch = 0;

        foreach (var team in teams)
        {
            var legs = team.Legs.Where(l => !string.IsNullOrWhiteSpace(l.SplitTime)).ToList();
            if (legs.Count == 0 || team.Time is null) { noLegsSource++; continue; }

            var hits = relayRows.Where(r =>
                    Same(r.Style, team.Style)
                    && SameDistance(r.Distance, team.Distance)
                    && r.Heat == team.Heat
                    && r.Lane == team.Lane
                    && SameTime(r.Time, team.Time))
                .ToList();
            if (hits.Count == 0) { unmatched++; continue; }
            if (hits.Count > 1) { ambiguous++; continue; }

            var row = hits[0];
            // Состава в базе нет — это работа RelayMemberBackfillService, а не доклейки:
            // заводить ноги здесь значило бы второй раз писать чужую логику.
            if (row.Legs.Count == 0) { noLegsDb++; continue; }

            // Тот же сторож, что в RelaySplitEnricher: состав сверяем мультимножеством годов
            // рождения. Не сошлось — строка базы про ДРУГУЮ команду, времена ей не принадлежат.
            var inDb = row.Legs.Select(l => l.BirthYear).OrderBy(y => y);
            var inSource = team.Legs.Select(l => l.BirthYear ?? 0).OrderBy(y => y);
            if (!inDb.SequenceEqual(inSource)) { yearMismatch++; continue; }

            attached++;
            foreach (var leg in legs)
            {
                var target = row.Legs.FirstOrDefault(l => l.LegOrder == leg.Order);
                if (target is null || target.SplitTime == leg.SplitTime) continue;
                legWrites.Add(new LegSplitWrite(target.MemberId, leg.SplitTime!));
            }
        }

        int swimsAttached = 0, swimsUnmatched = 0, swimsAmbiguous = 0;
        foreach (var swim in swims)
        {
            var hits = swimRows.Where(r =>
                    Same(r.Style, swim.Style)
                    && SameDistance(r.Distance, swim.Distance)
                    && Same(r.Gender, swim.Gender)
                    && SameTime(r.Time, swim.Time)
                    && r.BirthYear == swim.BirthYear)
                .ToList();
            if (hits.Count == 0) { swimsUnmatched++; continue; }
            if (hits.Count > 1) { swimsAmbiguous++; continue; }

            swimsAttached++;
            // Формат — тот же, что понимает клиентская ячейка времени (`time_split`).
            var value = string.Join(";", swim.Laps);
            if (hits[0].TimeSplit == value) continue;
            swimWrites.Add(new SwimSplitWrite(hits[0].ResultId, value));
        }

        var report = new SplitAttachReport(
            teams.Count, attached, noLegsSource, noLegsDb, unmatched, ambiguous, yearMismatch,
            swims.Count, swimsAttached, swimsUnmatched, swimsAmbiguous)
        {
            LegWrites = legWrites.Count,
            SwimWrites = swimWrites.Count,
        };
        return new SplitAttachPlan(legWrites, swimWrites, report);
    }

    private static bool Same(string? a, string? b) =>
        string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>«50» / «50m» / «4X50» — источник и база пишут дистанцию по-разному.</summary>
    private static bool SameDistance(string? a, string? b) =>
        string.Equals(a?.Trim().TrimEnd('m', 'M'), b?.Trim().TrimEnd('m', 'M'), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Время сверяем В МИЛЛИСЕКУНДАХ: «01:06.36» и «1:06.36» — одно и то же время, а как
    /// строки они разные (у базы формат протокола, у loglig — свой).
    /// </summary>
    private static bool SameTime(string? a, string? b) =>
        a is not null && b is not null
        && RelayLeadOffMatcher.ToMs(a) is long x && RelayLeadOffMatcher.ToMs(b) is long y && x == y;
}
