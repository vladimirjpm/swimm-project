namespace Swimm.API.Models;

public class Swimmer
{
    public int Id { get; set; }
    public string FullName { get; set; } = "";
    public int? BirthYear { get; set; }
    public string? Gender { get; set; }
    public int? ClubId { get; set; }
    public Club? Club { get; set; }
    public ICollection<ResultRecord> Results { get; set; } = new List<ResultRecord>();
}
