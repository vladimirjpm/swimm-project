namespace Swimm.Application.Dtos;

/// <summary>
/// Привязка импортируемого файла к многодневному событию.
/// EventId задан → дописываем день к существующему событию.
/// NewEventName задан (без EventId) → создаём новое событие с этим именем.
/// Оба null → обычное однодневное соревнование (поведение по умолчанию).
/// </summary>
public sealed record ImportEventOptions(int? EventId, string? NewEventName);

public class ImportResult
{
    public int TotalRows { get; set; }
    public int Created { get; set; }
    public int Skipped { get; set; }
    public int Errors { get; set; }
    public List<string> ErrorMessages { get; set; } = [];
    public List<string> DiagnosticLog { get; set; } = [];
    public string Message { get; set; } = string.Empty;
}

public class ClearResult
{
    public int Total { get; set; }
    public int Results { get; set; }
    public int Competitions { get; set; }
    public int CompetitionEvents { get; set; }
    public int Clubs { get; set; }
    public int Swimmers { get; set; }
    public int Relays { get; set; }
    public int Galleries { get; set; }
    public int GalleryItems { get; set; }
    public int Countries { get; set; }
    public int ImportHistory { get; set; }
}

public class DeleteCompetitionResult
{
    public int CompetitionId { get; set; }
    public string CompetitionName { get; set; } = string.Empty;
    public int Results { get; set; }
    public int Relays { get; set; }
    public int GalleryItems { get; set; }
    public int Galleries { get; set; }
    public int ImportHistory { get; set; }
    /// <summary>Удалённые URL-ы результатов (по OrgCompId), если этот OrgCompId больше нигде не использовался.</summary>
    public int ResultUrls { get; set; }
}
