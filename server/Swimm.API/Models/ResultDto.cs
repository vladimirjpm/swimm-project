namespace Swimm.API.Models;

public class ResultDto
{
    public int Id { get; set; }
    public string SwimmerName { get; set; } = "";
    public int? BirthYear { get; set; }
    public string? Gender { get; set; }
    public string? ClubName { get; set; }
    public string? CompetitionName { get; set; }
    public DateTime? CompetitionDate { get; set; }
    public string? StyleName { get; set; }
    public int? Distance { get; set; }
    public string? Time { get; set; }
    public int? Place { get; set; }
    public int? Points { get; set; }
    public string? PoolType { get; set; }
    public bool IsAward { get; set; }
}
