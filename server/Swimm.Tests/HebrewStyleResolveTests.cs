using Swimm.Parsing.Helpers;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Стиль из ивритского заголовка (<see cref="HebrewTextHelper.ResolveStyle"/>).
///
/// Живой баг, ради которого метод и появился: заголовок «3000 מטר חופשי» («3000 МЕТРОВ
/// вольным») искался в словаре целиком, не находился и уезжал в справочник <c>Styles</c>
/// как ключ `מטר_חופשי`. Витрины показывают только канонические стили, поэтому чемпионат
/// Израиля на 3 км в бассейне пропадал из селектора дисциплины целиком
/// (docs/data-integrity.md §9, решения 2026-08-26).
/// </summary>
public class HebrewStyleResolveTests
{
    private const string Hofshi = "חופשי";        // חופשי — вольный
    private const string Metr = "מטר";                      // מטר — метр
    private const string Knockout = "נוקאוט"; // נוקאוט — нокаут
    private const string Gav = "גב";                             // גב — спина
    private const string Meorav = "מעורב";        // מעורב — комплекс
    private const string Ishi = "אישי";                // אישי — личный

    [Fact]
    public void ExactHebrewNameStillResolves()
    {
        Assert.Equal("freestyle", HebrewTextHelper.ResolveStyle(Hofshi));
        Assert.Equal("backstroke", HebrewTextHelper.ResolveStyle(Gav));
    }

    [Fact]
    public void ExtraWordsAroundStyleDoNotCreateJunkKey()
    {
        // Оба ключа реально осели в базе: первый — с бассейнового чемпионата 3 км (#1540),
        // второй — с нокаут-раундов чемпионата в открытой воде (#1547).
        Assert.Equal("freestyle", HebrewTextHelper.ResolveStyle($"{Metr} {Hofshi}"));
        Assert.Equal("freestyle", HebrewTextHelper.ResolveStyle($"{Hofshi} {Knockout}"));
    }

    [Fact]
    public void TwoWordStyleWinsOverSingleWord()
    {
        // «מעורב אישי» — комплексное плавание, а одиночное «מעורב» словарь считает эстафетным
        // комплексом: пара токенов обязана проверяться раньше одиночного.
        Assert.Equal("individual_medley", HebrewTextHelper.ResolveStyle($"{Meorav} {Ishi}"));
    }

    [Fact]
    public void EnglishAndUnknownPassThroughUnchanged()
    {
        // Английские экспорты приходят уже нормальными; выдумывать стиль из мусора нельзя —
        // пусть импорт покажет неканонический ключ в диагностике, а не подставит «freestyle».
        Assert.Equal("Freestyle", HebrewTextHelper.ResolveStyle("Freestyle"));
        Assert.Equal("dolphin kick", HebrewTextHelper.ResolveStyle("dolphin kick"));
    }
}
