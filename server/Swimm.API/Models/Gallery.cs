using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Swimm.API.Models;

/// <summary>
/// Галерея медиа-элементов, привязанная к ResultRecord.
/// </summary>
public class Gallery
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public ICollection<GalleryItem> Items { get; set; } = new List<GalleryItem>();
}
