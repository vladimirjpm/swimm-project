namespace Swimm.API.Models;

public class Club
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? City { get; set; }
    public ICollection<Swimmer> Swimmers { get; set; } = new List<Swimmer>();
}
