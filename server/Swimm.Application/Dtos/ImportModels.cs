namespace Swimm.Application.Dtos;

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
    public int Clubs { get; set; }
    public int Swimmers { get; set; }
    public int Relays { get; set; }
    public int Galleries { get; set; }
    public int GalleryItems { get; set; }
    public int Countries { get; set; }
    public int ImportHistory { get; set; }
}
