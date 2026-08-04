using System.Reflection;
using Swimm.Application.Dtos;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Инвариант И11: **DTO, несущий время заплыва, несёт и признак его качества**
/// (docs/data-integrity.md, docs/plans/swim-time-quality-everywhere-plan.md).
///
/// Зачем тест, а не договорённость: признак качества нужен ВЕЗДЕ, где показано время, а
/// показано оно в дюжине мест. Перечислением экранов это не держится — сломается на первом
/// же новом DTO, и обнаружится не тогда, когда дёшево починить, а когда кто-то заметит
/// глазами, что витрина хвастается бессмыслицей (живой случай: карточка «Best swim»
/// соревнования показывала 200 вольным за 1:53.09 у 13-летнего).
///
/// Тест падает в момент добавления времени без качества. Если DTO законно не нуждается в
/// признаке — впиши его в <see cref="Exempt"/> С ОБОСНОВАНИЕМ. Пустых записей быть не должно:
/// белый список без причины через полгода неотличим от «просто чтобы тест позеленел».
/// </summary>
public class SwimTimeQualityInvariantTests
{
    /// <summary>Имена свойств, которые считаются «временем заплыва».</summary>
    private static readonly string[] TimeProps = ["Time", "TimeOriginal"];

    /// <summary>Имена свойств, которые считаются признаком качества.</summary>
    private static readonly string[] QualityProps = ["SuspectReason", "IssueReason", "Quality"];

    /// <summary>
    /// Законные исключения. Ключ — имя типа, значение — ПОЧЕМУ качество тут неприменимо.
    /// </summary>
    private static readonly Dictionary<string, string> Exempt = new()
    {
        // ── Админские экраны: у них СВОЙ UI качества (кнопка «⚑ Качество», реестр претензий
        //    к рекордам). Показывать значок тому, кто эту пометку и ставит, незачем.
        ["ResultEditDto"] = "админская форма правки результата — качество правится там же",
        ["ResultDuplicateRowDto"] = "админский список дубликатов в «Аномалиях»",
        ["RecordInputDto"] = "админский ввод рекорда",
        ["RecordQuickEditDto"] = "админская быстрая правка рекорда",
        ["RecordIssueDto"] = "САМА претензия: её время и есть оспариваемое значение",

        // ── Нормативы: планка разряда, а не чей-то заплыв. Оспаривать нечего — это не
        //    достижение конкретного человека, а таблица требований.
        ["NormativeStandardDto"] = "норматив разряда — планка, а не заплыв",
        ["NormativeStandardInputDto"] = "админский ввод норматива",
        ["StandardQuickEditDto"] = "админская быстрая правка норматива",

        // ── Данных ещё нет в БД: качество ставится ПОСЛЕ импорта, помечать нечего.
        ["ImportRecordPreviewRow"] = "строка файла на превью импорта — в БД её ещё нет",
        ["ParsedRecordDto"] = "разобранная строка внешней выгрузки рекордов до импорта",

        // ── Тренировки — наши собственные замеры: ни протокола, ни рекорда, «качество
        //    источника» к ним неприменимо (см. §3C плана).
        ["TrainingRowDto"] = "тренировочный замер, а не протокольный заплыв",

        // ── Служебное.
        ["RecordCandidateRow"] = "вход детектора рекордов, наружу не отдаётся",
        ["RecordAxes"] = "проекция строки справочника для разноса претензий по лестнице, не витрина",
        ["SuspectRowDto"] = "строка списка ПОМЕЧЕННЫХ — причина там отдельным полем Reason",
    };

    [Fact]
    public void EveryDtoWithSwimTime_CarriesQualityFlag()
    {
        var offenders = new List<string>();

        foreach (var type in typeof(ResultDto).Assembly.GetTypes())
        {
            if (!type.IsPublic || type.IsEnum || type.IsAbstract) continue;

            var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            var timeProp = props.FirstOrDefault(p =>
                TimeProps.Contains(p.Name) && p.PropertyType == typeof(string));
            if (timeProp is null) continue;

            if (props.Any(p => QualityProps.Contains(p.Name))) continue;
            if (Exempt.ContainsKey(type.Name)) continue;

            offenders.Add(type.Name);
        }

        Assert.True(offenders.Count == 0,
            "DTO несут время заплыва без признака качества (инвариант И11): "
            + string.Join(", ", offenders)
            + ". Добавь SuspectReason/IssueReason — или впиши тип в Exempt с обоснованием, "
            + "почему качество к нему неприменимо.");
    }

    [Fact]
    public void ExemptionList_HasNoEmptyJustifications()
    {
        // Белый список без причины — способ сделать тест зелёным, ничего не решив.
        var empty = Exempt.Where(e => string.IsNullOrWhiteSpace(e.Value)).Select(e => e.Key).ToList();
        Assert.True(empty.Count == 0, "Исключения без обоснования: " + string.Join(", ", empty));
    }

    [Fact]
    public void ExemptionList_HasNoStaleEntries()
    {
        // Тип переименовали или удалили, а строка в списке осталась — со временем список
        // превращается в свалку, и понять, что в нём живое, уже нельзя.
        var known = typeof(ResultDto).Assembly.GetTypes().Select(t => t.Name).ToHashSet();
        var stale = Exempt.Keys.Where(k => !known.Contains(k)).ToList();

        Assert.True(stale.Count == 0,
            "В белом списке типы, которых больше нет: " + string.Join(", ", stale));
    }
}
