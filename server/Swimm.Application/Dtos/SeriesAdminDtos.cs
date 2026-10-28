namespace Swimm.Application.Dtos;

// Админка серий турниров (/Admin/Series, docs/admin-pages/series.md): серия, её части, версии
// регламента с наградами и выпуски — дни соревнований, привязанные к частям.

/// <summary>Строка списка серий.</summary>
public sealed record SeriesRowDto(
    int Id, string Name, string? NameEn, string? MatchPhrases,
    int PartCount, int RegulationCount, int EditionDayCount);

/// <summary>Серия целиком — для страницы правки.</summary>
public sealed class SeriesEditDto
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public string? NameEn { get; init; }
    public string? MatchPhrases { get; init; }

    /// <summary>
    /// Общая часть названий привязанных выпусков — подсказка для <see cref="MatchPhrases"/>
    /// (§5.8 плана). null — выпусков меньше двух или общего нет.
    /// </summary>
    public string? SuggestedPhrase { get; init; }

    public IReadOnlyList<SeriesPartDto> Parts { get; init; } = [];
    public IReadOnlyList<SeriesRegulationDto> Regulations { get; init; } = [];
    public IReadOnlyList<SeriesEditionDayDto> EditionDays { get; init; } = [];
}

public sealed record SeriesPartDto(
    int Id, string Name, string? NameEn, int SortOrder, int? MinSeasonAge, int? MaxSeasonAge);

public sealed class SeriesRegulationDto
{
    public int Id { get; init; }
    public DateOnly EffectiveFrom { get; init; }
    public string? SourceUrl { get; init; }
    public string? Description { get; init; }
    public IReadOnlyList<SeriesAwardDto> Awards { get; init; } = [];
}

public sealed class SeriesAwardDto
{
    public int Id { get; init; }
    public int PartId { get; init; }
    public string PartName { get; init; } = "";
    public string Kind { get; init; } = "";
    public string Title { get; init; } = "";
    public int? CountBest { get; init; }
    public bool SplitByGender { get; init; }
    public int Places { get; init; }
    public int SortOrder { get; init; }
    public IReadOnlyList<SeriesAwardBandDto> Bands { get; init; } = [];
    public IReadOnlyList<SeriesAwardPrizeDto> Prizes { get; init; } = [];
}

/// <summary>Полоса: пол ("male"/"female"/null — оба) и сезонный возраст.</summary>
public sealed record SeriesAwardBandDto(string? Gender, int? MinSeasonAge, int? MaxSeasonAge);

public sealed record SeriesAwardPrizeDto(int Place, string Text);

/// <summary>День соревнования, привязанный к части серии.</summary>
public sealed record SeriesEditionDayDto(
    int CompetitionId, string Name, string Date, int? DayNumber, int? EventId,
    int PartId, string PartName, DateTime? RegulationCheckedAt, string? RegulationCheckedBy);

/// <summary>Часть серии в выпадающем списке: «רודובסקי/נימרי · צעירים».</summary>
public sealed record SeriesPartOptionDto(int PartId, string SeriesName, string PartName);

public sealed record SeriesInputDto(string Name, string? NameEn, string? MatchPhrases);

public sealed record SeriesPartInputDto(
    string Name, string? NameEn, int SortOrder, int? MinSeasonAge, int? MaxSeasonAge);

public sealed record SeriesRegulationInputDto(DateOnly EffectiveFrom, string? SourceUrl, string? Description);

public sealed record SeriesAwardInputDto(
    int PartId, string Title, int? CountBest, bool SplitByGender, int Places, int SortOrder,
    IReadOnlyList<SeriesAwardBandDto> Bands, IReadOnlyList<SeriesAwardPrizeDto> Prizes);

/// <summary>
/// Диагноз узнавания выпуска для одного дня (<c>ISeriesBinder</c>). SeriesId null — название
/// подошло под несколько серий.
/// </summary>
/// <param name="Share">Доля пловцов дня в возрастах выбранной (или лучшей) части.</param>
/// <param name="CurrentPartShare">Доля пловцов дня в возрастах ТЕКУЩЕЙ части; null — день не привязан.</param>
/// <param name="HasRegulation">У привязанного дня есть действующий на его дату регламент серии.</param>
/// <param name="BoundNow">BindAsync поставил привязку в этом вызове.</param>
public sealed record SeriesBindingDto(
    int CompetitionId, string CompetitionName, string Date, int? DayNumber,
    int? CurrentPartId, string? CurrentPartName,
    int? SeriesId, string? SeriesName,
    int? PickedPartId, string? PickedPartName,
    double Share, double? CurrentPartShare, string Reason,
    bool HasRegulation, DateTime? RegulationCheckedAt,
    bool BoundNow = false);

/// <summary>Итог мутации: Id созданной/изменённой строки или текст ошибки.</summary>
public sealed record SeriesSaveResult(bool Success, int Id, string? Error)
{
    public static SeriesSaveResult Ok(int id) => new(true, id, null);
    public static SeriesSaveResult Fail(string error) => new(false, 0, error);
}
