namespace Swimm.Application.Dtos;

/// <summary>
/// Карьерные (all-time) данные спортсмена для карточки в попапе
/// (переключатель «Соревнование / All-time», см. design_handoff_athlete_alltime).
/// </summary>
public class AthleteCareerDto
{
    /// <summary>Всего соревнований (distinct CompetitionId).</summary>
    public int Competitions { get; set; }

    /// <summary>Всего заплывов.</summary>
    public int Races { get; set; }

    /// <summary>Год первого результата (0 — данных нет).</summary>
    public int Since { get; set; }

    /// <summary>Сумма international points по всем заплывам.</summary>
    public int TotalPoints { get; set; }

    public int Gold { get; set; }
    public int Silver { get; set; }
    public int Bronze { get; set; }

    public List<CareerBestDto> BestByStyle { get; set; } = new();

    /// <summary>Разбивка медалей по конкретным заплывам (для тултипа "за что" на карточке).</summary>
    public List<MedalDetailDto> Medals { get; set; } = new();
}

/// <summary>Один медальный заплыв за карьеру — за что дали место 1/2/3.</summary>
public class MedalDetailDto
{
    /// <summary>1, 2 или 3.</summary>
    public int Position { get; set; }

    /// <summary>Например "Freestyle 50м".</summary>
    public string Note { get; set; } = string.Empty;

    public string Competition { get; set; } = string.Empty;

    /// <summary>Дата соревнования в исходном формате "DD/MM/YYYY".</summary>
    public string Date { get; set; } = string.Empty;
}

/// <summary>Лучшее время по (стиль × дистанция) за карьеру.</summary>
public class CareerBestDto
{
    public string Stroke { get; set; } = string.Empty;
    public string Distance { get; set; } = string.Empty;
    public string Time { get; set; } = string.Empty;
    public int Points { get; set; }
    public string Pool { get; set; } = string.Empty;
    public string Competition { get; set; } = string.Empty;

    /// <summary>Дата соревнования в исходном формате "DD/MM/YYYY" (как в ResultDto.Date).</summary>
    public string Date { get; set; } = string.Empty;

    /// <summary>Место на этом заплыве (для карточки-строки в стиле результатов соревнования).</summary>
    public int? Position { get; set; }
    public string Gender { get; set; } = string.Empty;
    public string EventStyleAge { get; set; } = string.Empty;
    public string AgeGroup { get; set; } = string.Empty;
    public bool IsMasters { get; set; }

    /// <summary>Award-eligible ли то соревнование (Competition.IsAward) — место 1/2/3 без этого не медаль.</summary>
    public bool IsAward { get; set; }
}
