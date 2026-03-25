namespace Swimm.API.Models;

public class Style
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public ICollection<ResultRecord> Results { get; set; } = new List<ResultRecord>();
}
