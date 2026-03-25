namespace Swimm.API.Models;

public class Competition
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public DateTime? Date { get; set; }
    public string? Location { get; set; }
    public string? PoolType { get; set; }
    public bool IsAward { get; set; }
    public ICollection<ResultRecord> Results { get; set; } = new List<ResultRecord>();
}
