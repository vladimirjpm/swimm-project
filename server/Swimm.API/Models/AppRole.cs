namespace Swimm.API.Models;

public class AppRole
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public ICollection<AppUserRole> UserRoles { get; set; } = new List<AppUserRole>();
}
