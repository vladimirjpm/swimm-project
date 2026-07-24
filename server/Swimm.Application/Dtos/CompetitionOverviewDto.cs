using System.Text.Json.Serialization;

namespace Swimm.Application.Dtos;

/// <summary>
/// Дэшборд соревнования для таба Overview (design_handoff_competition_overview, вариант 1b):
/// сводка, дни, лучший заплыв, топ-клубы (общий и по полу), топ-медалист. Всё вычислимо из
/// результатов, поэтому пустого дэшборда не бывает (пока есть хоть один результат).
/// Область = тот же источник, что у результатов: competitionId / eventId.
/// snake_case — как у CompetitionSourceDto (публичный API).
/// </summary>
public sealed class CompetitionOverviewDto
{
    [JsonPropertyName("summary")] public OverviewSummaryDto Summary { get; init; } = new();
    /// <summary>Дни события (для однодневного — один элемент). Порядок — по номеру дня/дате.</summary>
    [JsonPropertyName("days")] public IReadOnlyList<OverviewDayDto> Days { get; init; } = [];
    /// <summary>Лучший заплыв соревнования — максимум FINA-очков; null, если очков нет ни у кого.</summary>
    [JsonPropertyName("best_swim")] public OverviewBestSwimDto? BestSwim { get; init; }
    /// <summary>Клубный зачёт, топ-10 (полный — таб Clubs / /api/club-summary).</summary>
    [JsonPropertyName("top_clubs")] public IReadOnlyList<ClubSummaryDto> TopClubs { get; init; } = [];
    [JsonPropertyName("top_clubs_men")] public IReadOnlyList<ClubSummaryDto> TopClubsMen { get; init; } = [];
    [JsonPropertyName("top_clubs_women")] public IReadOnlyList<ClubSummaryDto> TopClubsWomen { get; init; } = [];
    /// <summary>Пловец с наибольшим числом медалей (личные заплывы, без эстафет).</summary>
    [JsonPropertyName("top_medalist")] public OverviewMedalistDto? TopMedalist { get; init; }
    /// <summary>Новые рекорды, установленные на соревновании. v1: всегда пусто — серверного
    /// сравнения результата с Record ещё нет (у Record нет FK на Competition); контракт
    /// зарезервирован, таб Records скрывается при пустом списке.</summary>
    [JsonPropertyName("records")] public IReadOnlyList<OverviewRecordDto> Records { get; init; } = [];
}

public sealed class OverviewSummaryDto
{
    [JsonPropertyName("result_count")] public int ResultCount { get; init; }
    [JsonPropertyName("day_count")] public int DayCount { get; init; }
    [JsonPropertyName("swimmer_count")] public int SwimmerCount { get; init; }
    [JsonPropertyName("club_count")] public int ClubCount { get; init; }
}

public sealed class OverviewDayDto
{
    [JsonPropertyName("competition_id")] public int CompetitionId { get; init; }
    /// <summary>Дата дня в формате dd/MM/yyyy (как Competition.Date).</summary>
    [JsonPropertyName("date")] public string Date { get; init; } = "";
    [JsonPropertyName("day_number")] public int? DayNumber { get; init; }
    [JsonPropertyName("sub_name")] public string? SubName { get; init; }
    [JsonPropertyName("result_count")] public int ResultCount { get; init; }
}

public sealed class OverviewBestSwimDto
{
    [JsonPropertyName("result_id")] public long ResultId { get; init; }
    [JsonPropertyName("swimmer_id")] public int SwimmerId { get; init; }
    [JsonPropertyName("first_name")] public string FirstName { get; init; } = "";
    [JsonPropertyName("last_name")] public string LastName { get; init; } = "";
    [JsonPropertyName("first_name_en")] public string FirstNameEn { get; init; } = "";
    [JsonPropertyName("last_name_en")] public string LastNameEn { get; init; } = "";
    [JsonPropertyName("club")] public string Club { get; init; } = "";
    [JsonPropertyName("style_name")] public string StyleName { get; init; } = "";
    [JsonPropertyName("distance")] public string Distance { get; init; } = "";
    [JsonPropertyName("gender")] public string Gender { get; init; } = "";
    [JsonPropertyName("time")] public string Time { get; init; } = "";
    [JsonPropertyName("international_points")] public int Points { get; init; }
    [JsonPropertyName("is_relay")] public bool IsRelay { get; init; }
    [JsonPropertyName("relay_team_name")] public string? RelayTeamName { get; init; }
    [JsonPropertyName("day_number")] public int? DayNumber { get; init; }
    [JsonPropertyName("competition_id")] public int CompetitionId { get; init; }
}

public sealed class OverviewMedalistDto
{
    [JsonPropertyName("swimmer_id")] public int SwimmerId { get; init; }
    [JsonPropertyName("first_name")] public string FirstName { get; init; } = "";
    [JsonPropertyName("last_name")] public string LastName { get; init; } = "";
    [JsonPropertyName("first_name_en")] public string FirstNameEn { get; init; } = "";
    [JsonPropertyName("last_name_en")] public string LastNameEn { get; init; } = "";
    [JsonPropertyName("club")] public string Club { get; init; } = "";
    [JsonPropertyName("gold")] public int Gold { get; init; }
    [JsonPropertyName("silver")] public int Silver { get; init; }
    [JsonPropertyName("bronze")] public int Bronze { get; init; }
}

/// <summary>Зарезервированный контракт карточки рекорда (v1 не заполняется).</summary>
public sealed class OverviewRecordDto
{
    [JsonPropertyName("kind")] public string Kind { get; init; } = "";
    [JsonPropertyName("style_name")] public string StyleName { get; init; } = "";
    [JsonPropertyName("distance")] public string Distance { get; init; } = "";
    [JsonPropertyName("gender")] public string Gender { get; init; } = "";
    [JsonPropertyName("time")] public string Time { get; init; } = "";
    [JsonPropertyName("holder_name")] public string HolderName { get; init; } = "";
    [JsonPropertyName("club")] public string? Club { get; init; }
    [JsonPropertyName("day_number")] public int? DayNumber { get; init; }
    [JsonPropertyName("result_id")] public long? ResultId { get; init; }
}
