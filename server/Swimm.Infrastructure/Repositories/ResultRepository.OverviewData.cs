using Microsoft.EntityFrameworkCore;
using Swimm.Domain.Entities;

namespace Swimm.Infrastructure.Repositories;

// Общие данные Overview: строки источника, его соревнования-дни и составы эстафет грузятся ОДИН
// раз на вызов, а карточки считают по ним в памяти (22.11.2026). Раньше каждая карточка ходила в
// Results сама — ~30 SQL на холодный вызов, из них полтора десятка по тем же строкам.
// Строк у одного соревнования — сотни, у многодневки — единицы тысяч: проекция без сущностей.
public partial class ResultRepository
{
    /// <summary>Всё, что карточкам Overview нужно из базы о строках выборки.</summary>
    private sealed class OverviewData
    {
        /// <summary>Строки выборки в порядке, в котором их отдала база (как раньше у каждой карточки).</summary>
        public required List<OverviewRow> Rows { get; init; }

        /// <summary>Соревнования (дни), у которых в выборке есть строки.</summary>
        public required Dictionary<int, OverviewCompetition> Competitions { get; init; }

        /// <summary>Ноги эстафет выборки: RelayId → участники в порядке базы.</summary>
        public required ILookup<int, OverviewRelayMember> RelayMembers { get; init; }

        public OverviewCompetition CompetitionOf(OverviewRow r) => Competitions[r.CompetitionId];
    }

    /// <summary>
    /// Строка результата для карточек Overview. Поля соревнования сюда не входят — они в
    /// <see cref="OverviewCompetition"/>: у всех строк дня одни и те же.
    /// </summary>
    private sealed class OverviewRow
    {
        public long Id { get; init; }
        public int CompetitionId { get; init; }
        public int SwimmerId { get; init; }
        public string FirstName { get; init; } = "";
        public string LastName { get; init; } = "";
        public string FirstNameEn { get; init; } = "";
        public string LastNameEn { get; init; } = "";
        public int BirthYear { get; init; }
        public int ClubId { get; init; }
        public bool ClubIsPseudo { get; init; }
        /// <summary>Как напечатано в протоколе (Р97): прежнее название, если оно есть.</summary>
        public string Club { get; init; } = "";
        public string ClubEn { get; init; } = "";
        /// <summary>Текущее название клуба — только у строк под прежним (ClubNameId != null).</summary>
        public string? ClubNow { get; init; }
        public int? RelayId { get; init; }
        public string? RelayTeamName { get; init; }
        public string StyleName { get; init; } = "";
        public string Distance { get; init; } = "";
        /// <summary>set — только для сухого расчёта «что изменится, если проставить пол» (Р126).</summary>
        public string Gender { get; set; } = "";
        public string AgeGroup { get; init; } = "";
        public string EventStyleAge { get; init; } = "";
        public DateTime CompetitionDate { get; init; }
        public int? Position { get; init; }
        public int? CombinedPlace { get; init; }
        public bool? IsBestResult { get; init; }
        public string? HeatType { get; init; }
        public string? Round { get; init; }
        public int? TimeMillisecond { get; init; }
        public string TimeOriginal { get; init; } = "";
        public bool TimeFail { get; init; }
        public int InternationalPoints { get; init; }
        public bool IsParaPoints { get; init; }
        public string? SuspectReason { get; init; }
    }

    /// <summary>Соревнование-день выборки: всё, что карточки читали через r.Competition.</summary>
    private sealed class OverviewCompetition
    {
        public int Id { get; init; }
        public string Date { get; init; } = "";
        public int? DayNumber { get; init; }
        public string? SubName { get; init; }
        /// <summary>«Результаты неполные» (Р114): видна, пока не принята как есть.</summary>
        public string? PartialResults { get; init; }
        public int? OrgCompId { get; init; }
        public bool IsAward { get; init; }
        public bool ShowCombineAllResults { get; init; }
        public bool IsMasters { get; init; }
        public string PoolType { get; init; } = "";
        public int? PointRuleClubsId { get; init; }
        public int? PointRuleSwimmersId { get; init; }
        public string? ClubPointsVerifiedKind { get; init; }
        public bool? HasOfficialClubStanding { get; init; }
        public bool ClubPointsDisabled { get; init; }
        public int? SeriesPartId { get; init; }
        public DateTime? SeriesRegulationCheckedAt { get; init; }
        public string? PartName { get; init; }
        public string? PartNameEn { get; init; }
        public int? SeriesId { get; init; }
        public string? SeriesName { get; init; }
        public string? SeriesNameEn { get; init; }
    }

    /// <summary>Нога эстафеты: пол и год рождения — самого пловца (эстафеты бывают смешанные).</summary>
    private sealed class OverviewRelayMember
    {
        public int Id { get; init; }
        public int RelayId { get; init; }
        public int SwimmerId { get; init; }
        public string FirstName { get; init; } = "";
        public string LastName { get; init; } = "";
        public string FirstNameEn { get; init; } = "";
        public string LastNameEn { get; init; } = "";
        public string? Gender { get; set; }
        public int BirthYear { get; init; }
    }

    /// <summary>
    /// Три запроса вместо полутора десятков: строки, их соревнования, составы эстафет.
    /// Составы грузим и при пустом списке эстафет — запрос ничего не вернёт, но таблица
    /// RelayMembers останется в метках записи кэша, как было, когда её читали карточки
    /// (метки ставятся по SQL сборки, CacheDependencyInterceptor).
    /// </summary>
    private async Task<OverviewData> LoadOverviewDataAsync(IQueryable<ResultRecord> query)
    {
        var rows = await query
            .Select(r => new OverviewRow
            {
                Id = r.Id,
                CompetitionId = r.CompetitionId,
                SwimmerId = r.SwimmerId,
                FirstName = r.Swimmer.FirstName,
                LastName = r.Swimmer.LastName,
                FirstNameEn = r.Swimmer.FirstNameEn,
                LastNameEn = r.Swimmer.LastNameEn,
                BirthYear = r.Swimmer.BirthYear,
                ClubId = r.ClubId,
                ClubIsPseudo = r.Club.IsPseudo,
                Club = r.ClubName != null ? r.ClubName.Name : r.Club.Name,
                ClubEn = r.ClubName != null ? r.ClubName.NameEn : r.Club.NameEn,
                ClubNow = r.ClubNameId != null ? r.Club.Name : null,
                RelayId = r.RelayId,
                RelayTeamName = r.Relay != null ? r.Relay.TeamName : null,
                StyleName = r.Style.Name,
                Distance = r.Distance,
                Gender = r.Gender,
                AgeGroup = r.AgeGroup,
                EventStyleAge = r.EventStyleAge,
                CompetitionDate = r.CompetitionDate,
                Position = r.Position,
                CombinedPlace = r.CombinedPlace,
                IsBestResult = r.IsBestResult,
                HeatType = r.HeatType,
                Round = r.Round,
                TimeMillisecond = r.TimeMillisecond,
                TimeOriginal = r.TimeOriginal,
                TimeFail = r.TimeFail,
                InternationalPoints = r.InternationalPoints,
                IsParaPoints = r.IsParaPoints,
                SuspectReason = r.SuspectReason
            })
            .ToListAsync();

        var competitionIds = rows.Select(r => r.CompetitionId).Distinct().ToList();
        var competitions = await _db.Competitions.AsNoTracking()
            .Where(c => competitionIds.Contains(c.Id))
            .Select(c => new OverviewCompetition
            {
                Id = c.Id,
                Date = c.Date,
                DayNumber = c.DayNumber,
                SubName = c.SubName,
                PartialResults = c.PartialResultsAcceptedAt == null ? c.PartialResultsEn : null,
                // Штамп многодневки стоит на событии (CompetitionIdentity) — фоллбек на него.
                OrgCompId = c.OrgCompId ?? (c.Event != null ? c.Event.OrgCompId : null),
                IsAward = c.IsAward,
                ShowCombineAllResults = c.ShowCombineAllResults,
                IsMasters = c.IsMasters,
                PoolType = c.PoolType,
                PointRuleClubsId = c.PointRuleClubsId,
                PointRuleSwimmersId = c.PointRuleSwimmersId,
                ClubPointsVerifiedKind = c.ClubPointsVerifiedKind,
                HasOfficialClubStanding = c.HasOfficialClubStanding,
                ClubPointsDisabled = c.ClubPointsDisabled,
                SeriesPartId = c.SeriesPartId,
                SeriesRegulationCheckedAt = c.SeriesRegulationCheckedAt,
                PartName = c.SeriesPart != null ? c.SeriesPart.Name : null,
                PartNameEn = c.SeriesPart != null ? c.SeriesPart.NameEn : null,
                SeriesId = c.SeriesPart != null ? (int?)c.SeriesPart.SeriesId : null,
                SeriesName = c.SeriesPart != null ? c.SeriesPart.Series.Name : null,
                SeriesNameEn = c.SeriesPart != null ? c.SeriesPart.Series.NameEn : null
            })
            .ToDictionaryAsync(c => c.Id);

        // Строки и дни — две команды без общего снимка (READ COMMITTED): день, удалённый между
        // ними (переимпорт, --split-days), оставил бы строки без соревнования, и любой
        // data.Competitions[...] в карточках упал бы KeyNotFoundException → 500. Такие строки
        // отбрасываем здесь, в одном месте: ответ — как если бы их уже не было.
        if (competitions.Count < competitionIds.Count)
            rows = rows.Where(r => competitions.ContainsKey(r.CompetitionId)).ToList();

        var relayIds = rows.Where(r => r.RelayId != null).Select(r => r.RelayId!.Value).Distinct().ToList();
        var members = await _db.RelayMembers.AsNoTracking()
            .Where(m => relayIds.Contains(m.RelayId))
            .Select(m => new OverviewRelayMember
            {
                Id = m.Id,
                RelayId = m.RelayId,
                SwimmerId = m.SwimmerId,
                FirstName = m.Swimmer.FirstName,
                LastName = m.Swimmer.LastName,
                FirstNameEn = m.Swimmer.FirstNameEn,
                LastNameEn = m.Swimmer.LastNameEn,
                Gender = m.Swimmer.Gender,
                BirthYear = m.Swimmer.BirthYear
            })
            .ToListAsync();

        return new OverviewData
        {
            Rows = rows,
            Competitions = competitions,
            RelayMembers = members.ToLookup(m => m.RelayId)
        };
    }
}
