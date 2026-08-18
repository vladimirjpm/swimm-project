using System.Text.Json.Serialization;

namespace Swimm.Application.Dtos;

/// <summary>
/// Публичное примечание к соревнованию: проза на трёх языках плюс табличка расхождения.
/// Языки отдаются ВСЕ сразу — попап-объяснялка на клиенте переключает их сам
/// (<c>UI_InfoPopup</c>), сервер про выбор читателя ничего не знает.
/// </summary>
/// <param name="Texts">Язык (<c>en</c>/<c>ru</c>/<c>he</c>) → текст. Языка нет — вкладка пустая.</param>
/// <param name="ScaleDiff">Строки «место / по регламенту / начислено»; пусто — только проза.</param>
public sealed record CompetitionNoteDto(
    [property: JsonPropertyName("texts")] IReadOnlyDictionary<string, string> Texts,
    [property: JsonPropertyName("scale_diff")] IReadOnlyList<ScaleDiffRowDto> ScaleDiff);

/// <summary>
/// Одна строка расхождения шкал. Хранится ДАННЫМИ, а не свёрстанной таблицей: цифры не
/// переводятся, а вёрстка из базы не знает ни про тёмную тему, ни про телефон.
/// </summary>
/// <param name="Place">Место в заплыве.</param>
/// <param name="Expected">Сколько очков даёт регламент (и наш расчёт).</param>
/// <param name="Actual">Сколько начислила официальная таблица.</param>
public sealed record ScaleDiffRowDto(
    [property: JsonPropertyName("place")] int Place,
    [property: JsonPropertyName("expected")] int Expected,
    [property: JsonPropertyName("actual")] int Actual);

/// <summary>Что админ вводит в форме примечания: тексты по языкам + строки расхождения.</summary>
public sealed class CompetitionNoteInputDto
{
    /// <summary>Язык → текст. Пустой/отсутствующий текст удаляет перевод.</summary>
    public IReadOnlyDictionary<string, string?> Texts { get; init; } = new Dictionary<string, string?>();

    /// <summary>Строки расхождения; пустой список стирает табличку.</summary>
    public IReadOnlyList<ScaleDiffRowDto> ScaleDiff { get; init; } = [];
}
