namespace Swimm.Parsing.Parsers.WaMastersRecords;

/// <summary>
/// Одна строка справочника «Masters World Records» World Aquatics, как она напечатана в PDF.
/// Ничего не нормализуем: перевод в оси <c>Record</c> (пол, бассейн, стиль, дата) — работа
/// <c>WaMastersRecordsSourceProvider</c>, чтобы парсер оставался про «что написано в файле».
/// </summary>
/// <param name="PoolCode">«LCM» / «SCM» — как в заголовке блока.</param>
/// <param name="Gender">«Women» / «Men» (эстафетный «Mixed» в личных файлах не встречается).</param>
/// <param name="Distance">«50», «100», … «1500» — без «m».</param>
/// <param name="StyleName">«Freestyle», «Backstroke», «Breaststroke», «Butterfly», «Medley».</param>
/// <param name="AgeKey">Возрастная полоса «25-29» … «105-109».</param>
/// <param name="Time">Время как в файле: «25.37», «02:02.06», у столетних бывает «01:14:08.7».</param>
/// <param name="Country">Колонка NF — alpha-3 федерации рекордсмена.</param>
/// <param name="Athlete">Колонка Athlete: «ФАМИЛИЯ Имя», как печатает источник.</param>
/// <param name="RecordDate">Колонка Date в формате источника: «24 Aug 2024».</param>
public sealed record WaMastersRecordRow(
    string PoolCode,
    string Gender,
    string Distance,
    string StyleName,
    string AgeKey,
    string Time,
    string Country,
    string Athlete,
    string RecordDate);
