using System.Text.Json.Serialization;

namespace Swimm.Application.Dtos;

/// <summary>
/// Публичное примечание к соревнованию: проза на трёх языках плюс табличка расхождения.
/// Языки отдаются ВСЕ сразу — попап-объяснялка на клиенте переключает их сам
/// (<c>UI_InfoPopup</c>), сервер про выбор читателя ничего не знает.
/// </summary>
/// <param name="Texts">Язык (<c>en</c>/<c>ru</c>/<c>he</c>) → текст. Языка нет — вкладка пустая.</param>
/// <param name="ScaleDiff">Строки «место / кто / по регламенту / начислено»; пусто — только проза.</param>
/// <param name="SourceUrl">Ссылка на регламент соревнования; null — источник не указан.</param>
/// <param name="ScaleDiffCaption">Какой заплыв разобран в табличке; null — подписи нет.</param>
public sealed record CompetitionNoteDto(
    [property: JsonPropertyName("texts")] IReadOnlyDictionary<string, string> Texts,
    [property: JsonPropertyName("scale_diff")] IReadOnlyList<ScaleDiffRowDto> ScaleDiff,
    [property: JsonPropertyName("source_url")] string? SourceUrl = null,
    [property: JsonPropertyName("scale_diff_caption")] string? ScaleDiffCaption = null);

/// <summary>
/// Одна строка расхождения шкал. Хранится ДАННЫМИ, а не свёрстанной таблицей: цифры не
/// переводятся, а вёрстка из базы не знает ни про тёмную тему, ни про телефон.
/// </summary>
/// <param name="Place">Место в заплыве.</param>
/// <param name="Expected">Сколько очков даёт регламент (и наш расчёт).</param>
/// <param name="Actual">Сколько начислила официальная таблица.</param>
/// <param name="Subject">
/// Кому достались очки — клуб или пловец, как в протоколе. Без этого столбца перестановку
/// не прочитать: «за 9-е место 25 очков» становится доказательством только когда видно,
/// что они ушли конкретному клубу. null — старые заметки, где разбиралась только шкала.
/// </param>
/// <param name="Heat">
/// Номер заплыва. Часто это и есть ПРИЧИНА расхождения: у 1581 официальные очки розданы
/// по номеру заплыва вместо места, и утверждать это, не показывая номер, — просить верить
/// на слово. null — причина в другом (например, в самой шкале).
/// </param>
/// <param name="Time">
/// Время строки, как напечатано в протоколе. Доказывает, что места расставлены верно и
/// спорны именно очки: без него читатель не может проверить порядок. null — не указано.
/// </param>
public sealed record ScaleDiffRowDto(
    [property: JsonPropertyName("place")] int Place,
    [property: JsonPropertyName("expected")] int Expected,
    [property: JsonPropertyName("actual")] int Actual,
    [property: JsonPropertyName("subject")] string? Subject = null,
    [property: JsonPropertyName("heat")] int? Heat = null,
    [property: JsonPropertyName("time")] string? Time = null);

/// <summary>Что админ вводит в форме примечания: тексты по языкам + строки расхождения.</summary>
public sealed class CompetitionNoteInputDto
{
    /// <summary>Язык → текст. Пустой/отсутствующий текст удаляет перевод.</summary>
    public IReadOnlyDictionary<string, string?> Texts { get; init; } = new Dictionary<string, string?>();

    /// <summary>Строки расхождения; пустой список стирает табличку.</summary>
    public IReadOnlyList<ScaleDiffRowDto> ScaleDiff { get; init; } = [];

    /// <summary>Ссылка на регламент; пустая строка стирает её.</summary>
    public string? SourceUrl { get; init; }

    /// <summary>Подпись таблички («какой заплыв»); пустая строка стирает её.</summary>
    public string? ScaleDiffCaption { get; init; }
}
