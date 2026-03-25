namespace Swimm.API.Models;

public class GalleryItem
{
    public int Id { get; set; }
    public int GalleryId { get; set; }
    public Gallery Gallery { get; set; } = null!;
    public string Url { get; set; } = "";
    public string? Caption { get; set; }
    public int SortOrder { get; set; }
}
