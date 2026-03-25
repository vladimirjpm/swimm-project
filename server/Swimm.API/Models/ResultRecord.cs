namespace Swimm.API.Models;

public class ResultRecord
{
    public int Id { get; set; }
    public int SwimmerId { get; set; }
    public Swimmer Swimmer { get; set; } = null!;
    public int CompetitionId { get; set; }
    public Competition Competition { get; set; } = null!;
    public int? StyleId { get; set; }
    public Style? Style { get; set; }
    public int? Distance { get; set; }
    public string? Time { get; set; }
    public int? Place { get; set; }
    public int? Points { get; set; }
    public int? RelayId { get; set; }
    public Relay? Relay { get; set; }
}
