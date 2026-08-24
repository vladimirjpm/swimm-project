namespace Swimm.Application.Dtos;

/// <summary>
/// «Сколько рекордов побьёт этот файл» — считается на превью, ДО «Применить»
/// (docs/data-integrity.md §12, Б2).
///
/// Зачем: битый рекорд — самый громкий симптом кривого протокола (эталон — 00:32.59 на
/// 100 баттерфляем у Маккабиады, и 01:53.09 на 200 вольным у 13-летнего). Настоящий рекорд
/// в файле — событие редкое; десяток «рекордов» разом почти всегда значит, что протокол
/// разобрался неверно. Увидеть это до записи в БД дешевле, чем разбирать потом.
///
/// ⚠ Диагноз, а не запрет: импорт не блокируется. Рекорды случаются.
/// </summary>
public sealed class ImportRecordPreviewDto
{
    /// <summary>Сколько рекордов побьёт файл. 0 — обычная картина.</summary>
    public int Count { get; set; }

    /// <summary>
    /// Строки для показа человеку (обрезано — в UI нужен не список, а сигнал).
    /// </summary>
    public List<ImportRecordPreviewRow> Rows { get; set; } = [];

    /// <summary>
    /// Проверка не выполнилась (справочник рекордов пуст, файл не разобрался).
    /// Отличается от Count = 0: «не смотрели» это не «чисто».
    /// </summary>
    public string? Error { get; set; }
}

/// <summary>Один побиваемый рекорд в превью импорта.</summary>
public sealed class ImportRecordPreviewRow
{
    /// <summary>
    /// Порядковый номер строки в разобранном файле — адрес заплыва ДО того, как у него
    /// появился Id в БД. По нему галочка «пометить сомнительным» в превью попадает ровно
    /// в тот заплыв, который человек видел на экране (см. DiscoveryAdminController.Import).
    /// Один заплыв может бить два рекорда (open + возрастной) — тогда две строки превью
    /// несут ОДИН RowIndex.
    /// </summary>
    public int RowIndex { get; set; }

    /// <summary>«Age 13 record» / «Open record» / «Masters 45-49».</summary>
    public string Kind { get; set; } = "";
    public string SwimmerName { get; set; } = "";
    public string Club { get; set; } = "";
    public string StyleName { get; set; } = "";
    public string Distance { get; set; } = "";
    public string Gender { get; set; } = "";

    /// <summary>Время из файла.</summary>
    public string Time { get; set; } = "";

    /// <summary>Действующий рекорд, который оно перебивает, и чей он.</summary>
    public string RecordTime { get; set; } = "";
    public string RecordHolder { get; set; } = "";

    /// <summary>Год рождения из протокола — по нему пловец сопоставляется с БД (тёзки).</summary>
    public int? BirthYear { get; set; }

    /// <summary>Бассейн («25m»/«50m») — карточка loglig держит рекорды отдельно по длине.</summary>
    public string PoolType { get; set; } = "";
}
