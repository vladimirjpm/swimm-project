using System.Text.Json.Serialization;

namespace Swimm.Application.Dtos;

/// <summary>Нашёлся ли пловец строки превью в нашей БД.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PreviewSwimmerMatch>))]
public enum PreviewSwimmerMatch
{
    /// <summary>В БД такого нет — соревнование ещё не импортировано, пловец появится вместе с ним.</summary>
    None,

    /// <summary>Ровно один — с ним и работаем (loglig-id, пол).</summary>
    One,

    /// <summary>Тёзки: несколько с тем же именем и годом. Гадать нельзя, выбирает человек.</summary>
    Many
}

/// <summary>Что сказала карточка loglig про подозрительный заплыв.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<RecordCheckVerdict>))]
public enum RecordCheckVerdict
{
    /// <summary>Не проверяли: пловец не привязан к loglig либо карточка недоступна.</summary>
    NotChecked,

    /// <summary>На loglig ровно это время — время из протокола настоящее.</summary>
    Confirms,

    /// <summary>На loglig другое (худшее) время — очень похоже, что протокол разобрался неверно.</summary>
    Contradicts,

    /// <summary>Карточка есть, но этой дистанции в ней нет — сверять не с чем.</summary>
    NoData
}

/// <summary>
/// Одна строка проверки «рекорд настоящий или разбор поехал» для превью затягивания.
/// Считается ЛЕНИВО, отдельным запросом после показа превью: карточки loglig — это по
/// запросу на пловца, и тормозить ими сам разбор (тем более пакетный) нельзя.
/// </summary>
/// <param name="RowIndex">Строка в разобранном файле — тот же адрес, что у галочки «сомнительный».</param>
/// <param name="SwimmerName">Имя из протокола — чтобы панель нашла свою строку.</param>
/// <param name="Match">Нашёлся ли пловец в БД.</param>
/// <param name="SwimmerId">Кому привязывать loglig-id и пол; null — привязывать некому.</param>
/// <param name="LogligId">Уже привязанный id.</param>
/// <param name="LogligUrl">Ссылка на карточку — с сезоном ЭТОГО соревнования и вкладкой результатов.</param>
/// <param name="Gender">Пол пловца в БД (null — не заполнен, его тут же можно проставить).</param>
/// <param name="Verdict">Итог сверки времени с карточкой.</param>
/// <param name="Message">Человеческая формулировка: что нашли и что рекомендуем.</param>
/// <param name="SuggestedLogligId">
/// Id, вытащенный ИЗ САМОГО ПРОТОКОЛА (на странице заплыва loglig имя — ссылка на карточку).
/// Есть даже у пловца, которого в нашей базе ещё нет: тогда проверить время можно, а
/// привязать пока некому. Если пловец в базе есть и не привязан — панель предлагает
/// привязать этот id одной кнопкой, вводить руками ничего не надо.
/// </param>
public sealed record PreviewRecordCheckRow(
    int RowIndex,
    string SwimmerName,
    PreviewSwimmerMatch Match,
    int? SwimmerId,
    int? LogligId,
    string? LogligUrl,
    string? Gender,
    RecordCheckVerdict Verdict,
    string Message,
    int? SuggestedLogligId = null);
