namespace Swimm.API.Models;

public class UserExternalLogin
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public AppUser User { get; set; } = null!;
    public string Provider { get; set; } = "";
    public string ProviderKey { get; set; } = "";
}
