using System.Text.Json.Serialization;

namespace Swimm.Application.Dtos;

/// <summary>Итоговое состояние реакции после тоггла (для оптимистичного UI).</summary>
public class ReactionStateDto
{
    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("mine")]
    public bool Mine { get; set; }
}
