using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Swimm.API.Models;

/// <summary>
/// Элемент галереи (изображение или видео).
/// </summary>
public class GalleryItem
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int GalleryId { get; set; }

    [ForeignKey(nameof(GalleryId))]
    public Gallery Gallery { get; set; } = null!;

    /// <summary>image, video</summary>
    [MaxLength(10)]
    public string Type { get; set; } = string.Empty;

    /// <summary>youtube, vimeo, other</summary>
    [MaxLength(20)]
    public string? SourceType { get; set; }

    [MaxLength(500)]
    public string? Url { get; set; }

    [MaxLength(500)]
    public string? Code { get; set; }
}
