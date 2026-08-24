using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Loglig-id участников — из САМОГО протокола: на странице заплыва имя напечатано ссылкой
/// на карточку. Это единственный способ узнать id пловца, которого в нашей базе ещё нет
/// (соревнование не импортировано).
///
/// ⚠ Ссылки есть только в НЕмодальной версии страницы: `?showCategories=True` без
/// `isModal=True`. Модальная (печатная) отдаёт те же данные, но имена в ней — обычный текст.
/// Снапшоты живых страниц лежат в Fixtures/Loglig, в сеть тесты не ходят.
/// </summary>
public class LogligParticipantsParseTests
{
    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Loglig", name));

    [Fact]
    public void ParsesLinkedParticipants()
    {
        // Чемпионат Израиля «ארנה» зима-2025 (loglig 13627), заплыв 1500 вольным.
        var rows = LogligClient.ParseEventParticipants(Fixture("loglig-event-72968-links.html"));

        Assert.NotEmpty(rows);
        var emily = Assert.Single(rows, r => r.LogligId == 109926);
        Assert.Equal("אמילי גולוס", emily.FullName);
        Assert.Equal(2008, emily.BirthYear);
    }

    [Fact]
    public void ModalPageHasNoLinks_SoNothingIsInvented()
    {
        // Печатная версия — та же таблица, но имена не ссылки: id взять неоткуда, и
        // выдумывать его нельзя.
        Assert.Empty(LogligClient.ParseEventParticipants(Fixture("loglig-event-82997-individual.html")));
    }

    [Fact]
    public void ReadsBirthYear_EvenWhenNameHasDoubleSpace()
    {
        // Живой случай: «אליה  מאשה גדול» напечатана с двойным пробелом. Ячейки таблицы
        // пробелы схлопывают — если имя не схлопнуть так же, оно «не находится» в своей же
        // строке, и год рождения (соседняя колонка) теряется вместе с сопоставлением.
        const string html = """
            <tr>
              <td>1</td>
              <td><a href="/Players/Details/424242?seasonId=1605"> אליה  מאשה גדול </a></td>
              <td>2011</td>
              <td>Hapoel</td>
            </tr>
            """;

        var row = Assert.Single(LogligClient.ParseEventParticipants(html));

        Assert.Equal(424242, row.LogligId);
        Assert.Equal("אליה מאשה גדול", row.FullName);
        Assert.Equal(2011, row.BirthYear);
    }

    [Fact]
    public void ParticipantKey_IgnoresTokenOrder()
    {
        // На сайте «имя фамилия», в протоколе бывает наоборот — это один человек.
        Assert.Equal(
            LogligClient.ParticipantKey("אמילי גולוס", 2008),
            LogligClient.ParticipantKey("גולוס אמילי", 2008));
    }

    [Fact]
    public void ParticipantKey_SeparatesNamesakesByBirthYear()
    {
        Assert.NotEqual(
            LogligClient.ParticipantKey("אמילי גולוס", 2008),
            LogligClient.ParticipantKey("אמילי גולוס", 2011));
    }

    [Fact]
    public void ParticipantKey_NormalizesFinalLettersAndGeresh()
    {
        // Та же нормализация, что у дедупа: финальные ивритские буквы и гереш.
        Assert.Equal(
            LogligClient.ParticipantKey("אנג׳לה כהן", 2010),
            LogligClient.ParticipantKey("אנג'לה כהן", 2010));
    }
}
