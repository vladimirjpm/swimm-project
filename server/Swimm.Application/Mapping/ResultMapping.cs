using System.Linq.Expressions;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;

namespace Swimm.Application.Mapping;

/// <summary>
/// Единая проекция результата заплыва в DTO.
/// Это <see cref="Expression"/>, а не обычная функция, — чтобы EF Core транслировал её
/// в SQL (JOIN на справочники) внутри <c>.Select(...)</c>, без загрузки сущностей в память.
/// </summary>
public static class ResultMapping
{
    public static readonly Expression<Func<ResultRecord, ResultDto>> ToDto = r => new ResultDto
    {
        Id = r.Id,
        Country = r.Competition.Country,
        CompetitionName = r.Competition.Name,
        IsMasters = r.Competition.IsMasters,
        IsAward = r.Competition.IsAward,
        AgeGroup = r.AgeGroup,
        Date = r.Competition.Date,
        EventId = r.Competition.EventId,
        EventName = r.Competition.Event != null ? r.Competition.Event.Name : null,
        DayNumber = r.Competition.DayNumber,
        SubName = r.Competition.SubName,
        // Event ОТЛОЖЕН: в БД не хранится — остаётся пустым (см. ResultDto.Event).
        StyleName = r.Style.Name,
        Distance = r.Distance,
        Gender = r.Gender,
        EventStyleAge = r.EventStyleAge,
        PoolType = r.Competition.PoolType,
        Position = r.Position,
        PositionAgeGroup = r.PositionAgeGroup,
        Heat = r.Heat,
        Lane = r.Lane,
        SwimmerId = r.SwimmerId,
        LastName = r.Swimmer.LastName,
        FirstName = r.Swimmer.FirstName,
        LastNameEn = r.Swimmer.LastNameEn,
        FirstNameEn = r.Swimmer.FirstNameEn,
        BirthYear = r.Swimmer.BirthYear,
        ClubName = r.Club.Name,
        ClubNameEn = r.Club.NameEn,
        TimeMillisecond = r.TimeMillisecond,
        TimeOriginal = r.TimeOriginal,
        TimeSplit = r.TimeSplit,
        TimeFail = r.TimeFail,
        TimeFailNote = r.TimeFailNote,
        InternationalPoints = r.InternationalPoints,
        Note = r.Note,
        IsRelay = r.RelayId != null,
        RelayTeamName = r.Relay != null ? r.Relay.TeamName : null,
        RelaySwimmersName = r.Relay != null ? r.Relay.SwimmersName : null,
        Gallery = r.Gallery != null
            ? r.Gallery.Items.Select(i => new GalleryItemDto
            {
                Type = i.Type,
                SourceType = i.SourceType,
                Url = i.Url,
                Code = i.Code
            }).ToList()
            : null
    };
}
