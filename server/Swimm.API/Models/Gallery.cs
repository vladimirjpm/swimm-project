namespace Swimm.API.Models;

public class Gallery
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<GalleryItem> Items { get; set; } = new List<GalleryItem>();
}
