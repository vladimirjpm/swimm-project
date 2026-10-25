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

/// <summary>
/// 🎉 одного заплыва для витрины (протокол, страница пловца, группа). В ответ попадают только
/// заплывы, где поздравления есть или поставлено моё: пустой счётчик витрина не рисует вовсе
/// (решение Влада 25.10.2026), а редкие строки держат ответ маленьким.
/// </summary>
public class CheerStateDto : ReactionStateDto
{
    [JsonPropertyName("result_id")]
    public long ResultId { get; set; }
}
