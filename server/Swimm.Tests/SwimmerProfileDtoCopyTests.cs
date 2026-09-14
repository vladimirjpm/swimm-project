using System.Text.Json;
using Swimm.Application.Dtos;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Страница пловца дописывает поля шапки (сезоны, рекорды, программы) в КОПИЮ профиля, а не в
/// объект из кэша (docs/plans/cache-row-precision-plan.md, К4б.0; правило §4-3 cache-tags-plan):
/// в памяти правка объекта из кэша видна всем следующим читателям, в Redis — нет.
/// </summary>
public class SwimmerProfileDtoCopyTests
{
    [Fact]
    public void CopyForPage_CopiesEveryField_AndEditingTheCopyLeavesTheCachedProfileAlone()
    {
        var cached = new SwimmerProfileDto
        {
            Id = 5825,
            FullName = "Сабина Барцева",
            FirstName = "Сабина",
            LastName = "Барцева",
            FirstNameEn = "Sabina",
            LastNameEn = "Bartseva",
            BirthYear = 2014,
            Gender = "F",
            ClubId = 438,
            ClubName = "הפועל דולפין נתניה",
            CountryCode = "ISR",
            CountryName = "Israel",
            AvatarUrl = "avatar.png",
            Origin = "isr",
            Programs = ["pool"],
            Records = [new SwimmerHeldRecordDto()],
            Seasons = [new SwimmerSeasonOptionDto()],
        };
        var before = JsonSerializer.Serialize(cached);

        var copy = cached.CopyForPage();

        Assert.NotSame(cached, copy);
        // Все поля — через JSON: новое поле профиля, забытое в копии, уронит тест само.
        Assert.Equal(before, JsonSerializer.Serialize(copy));

        // Так страница дописывает шапку — объекту из кэша это не видно.
        copy.Seasons.Add(new SwimmerSeasonOptionDto());
        copy.Records.Clear();
        copy.Programs.Add("open");
        copy.AgeInSeason = 12;
        copy.RecordsHeld = 3;

        Assert.Equal(before, JsonSerializer.Serialize(cached));
    }
}
