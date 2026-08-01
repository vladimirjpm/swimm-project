namespace Swimm.Domain.Entities;

/// <summary>
/// Результат сверки строки справочника рекордов с нашими протоколами: нашёлся ли заплыв
/// с таким временем на этой оси (стиль × дистанция × бассейн × пол), и какой именно.
///
/// ⚠ ГЛАВНОЕ: <see cref="Found"/> = false — это НЕ признак ошибки источника. У нас
/// загружены протоколы не за все годы, и рекорд 1995 года просто не с чем сверять.
/// «Не найдено» означает ровно «мы пока не можем подтвердить», и в UI это должно быть
/// написано именно так. Претензии живут отдельно — в <see cref="RecordIssue"/>.
///
/// Одна строка на строку <see cref="Record"/>; пересчитывается целиком командой
/// «Сверить с протоколами» (дашборд /Admin). См. docs/plans/records-quality-plan.md.
/// </summary>
public class RecordVerification
{
    /// <summary>Ключ = Id рекорда: сверка живёт ровно столько, сколько сам рекорд.</summary>
    public int RecordId { get; set; }

    public Record? Record { get; set; }

    /// <summary>Нашёлся ли заплыв с этим временем на этой оси.</summary>
    public bool Found { get; set; }

    /// <summary>Найденный заплыв (Results.Id — long, как у ResultRecord); null — не найден.</summary>
    public long? ResultId { get; set; }

    /// <summary>Пловец найденного заплыва — по нему можно проверить, тот ли это человек.</summary>
    public int? SwimmerId { get; set; }

    /// <summary>
    /// Совпала ли ещё и дата рекорда. false при <see cref="Found"/> = true означает, что
    /// время нашлось, но в другой день — повод посмотреть глазами, а не приговор.
    /// null — в источнике даты нет либо она в неразобранном формате.
    /// </summary>
    public bool? DateMatched { get; set; }

    public DateTime CheckedAt { get; set; }
}
