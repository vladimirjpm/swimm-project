using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Swimm.Application.Abstractions;
using Swimm.Application.Constants;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Витринный сезон по данным (<see cref="ShowcaseSeasonProvider"/>, docs/season-boundary-rule.md).
///
/// Сама арифметика правила проверена в <see cref="ShowcaseSeasonTests"/>; здесь — то, что
/// провайдер кладёт на её вход: только ЗИМНИЕ чемпионаты (лига, лето и открытая вода границу
/// не двигают) и кэш.
///
/// ⚠ «Сейчас» всегда передаётся явно (<c>StartYearAtAsync</c>): тест с <c>DateTime.UtcNow</c>
/// протухал бы на переходе года — ровно тот сорт незаметности, из-за которого баг 01.09.2026
/// прожил месяц.
/// </summary>
public class ShowcaseSeasonProviderTests
{
    private static SwimmReadDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmReadDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    /// <summary>Кэш, который действительно хранит — нужен тесту про повторный вызов.</summary>
    private sealed class MemoryCache : ICacheService
    {
        private readonly Dictionary<string, object?> _store = new();

        public Task<T?> GetAsync<T>(string key) =>
            Task.FromResult(_store.TryGetValue(key, out var v) ? (T?)v : default);

        public Task SetAsync<T>(string key, T value, TimeSpan ttl)
        {
            _store[key] = value;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key)
        {
            _store.Remove(key);
            return Task.CompletedTask;
        }

        public Task InvalidateAllAsync()
        {
            _store.Clear();
            return Task.CompletedTask;
        }
    }

    private sealed class NullCache : ICacheService
    {
        public Task<T?> GetAsync<T>(string key) => Task.FromResult<T?>(default);
        public Task SetAsync<T>(string key, T value, TimeSpan ttl) => Task.CompletedTask;
        public Task RemoveAsync(string key) => Task.CompletedTask;
        public Task InvalidateAllAsync() => Task.CompletedTask;
    }

    private static Competition Comp(
        string name, string date, string pool = PoolTypes.Short,
        bool championship = false, string? overrideKind = null) => new()
        {
            Name = name,
            Date = date,
            PoolType = pool,
            IsChampionship = championship,
            IsAward = true,
            IsMasters = false,
            StandingKindOverride = overrideKind,
        };

    /// <summary>
    /// САМ БАГ 01.09.2026: календарный сезон уже 2026/27, а зимнего чемпионата в нём ещё не
    /// было — витрина обязана остаться на 2025/26. Раньше здесь стоял
    /// <c>SeasonMath.CurrentStartYear()</c>, и карточки на витрине опустели.
    /// </summary>
    [Fact]
    public async Task FirstOfSeptember_StaysOnPreviousSeason()
    {
        await using var db = CreateDb(nameof(FirstOfSeptember_StaysOnPreviousSeason));
        db.AddRange(
            Comp("Winter 2024/25", "20/02/2025", championship: true),
            Comp("Winter 2025/26", "26/02/2026", championship: true));
        await db.SaveChangesAsync();

        var provider = new ShowcaseSeasonProvider(db, new NullCache());

        Assert.Equal(2025, await provider.StartYearAtAsync(new DateTime(2026, 9, 1)));
        Assert.Equal(2025, await provider.StartYearAtAsync(new DateTime(2026, 12, 20)));
    }

    /// <summary>Зимний чемпионат проплыли — витрина переезжает на новый сезон.</summary>
    [Fact]
    public async Task AfterWinterChampionship_SwitchesToNewSeason()
    {
        await using var db = CreateDb(nameof(AfterWinterChampionship_SwitchesToNewSeason));
        db.AddRange(
            Comp("Winter 2025/26", "26/02/2026", championship: true),
            Comp("Winter 2026/27", "18/02/2027", championship: true));
        await db.SaveChangesAsync();

        var provider = new ShowcaseSeasonProvider(db, new NullCache());

        Assert.Equal(2025, await provider.StartYearAtAsync(new DateTime(2027, 2, 17)));
        Assert.Equal(2026, await provider.StartYearAtAsync(new DateTime(2027, 3, 1)));
    }

    /// <summary>
    /// Границу двигает только зимний чемпионат: лето, лига и открытая вода — нет.
    /// (Перенесено из SwimmerPageRepositoryTests вместе с самим расчётом.)
    /// </summary>
    [Fact]
    public async Task OnlyWinterChampionshipsMoveTheBorder()
    {
        await using var db = CreateDb(nameof(OnlyWinterChampionshipsMoveTheBorder));
        db.AddRange(
            Comp("Summer champs", "15/07/2026", PoolTypes.Long, championship: true),
            Comp("League", "10/12/2025"),                                     // не чемпионат
            Comp("Open water champs", "27/04/2026", championship: true,
                overrideKind: StandingKinds.OpenWater));                       // роль переопределена
        await db.SaveChangesAsync();

        var provider = new ShowcaseSeasonProvider(db, new NullCache());

        // Ни одного зимнего чемпионата — фолбэк календарный (прятать данные, потому что
        // нечем подтвердить границу, хуже, чем показать их).
        Assert.Equal(2026, await provider.StartYearAtAsync(new DateTime(2026, 9, 1)));
    }

    /// <summary>
    /// Даты берутся из кэша: за один запрос страницы витринный сезон спрашивают несколько
    /// репозиториев подряд. Проверяем через данные, добавленные ПОСЛЕ первого вызова.
    /// </summary>
    [Fact]
    public async Task WinterDates_AreCached()
    {
        await using var db = CreateDb(nameof(WinterDates_AreCached));
        db.Add(Comp("Winter 2025/26", "26/02/2026", championship: true));
        await db.SaveChangesAsync();

        var provider = new ShowcaseSeasonProvider(db, new MemoryCache());
        Assert.Equal(2025, await provider.StartYearAtAsync(new DateTime(2027, 3, 1)));

        db.Add(Comp("Winter 2026/27", "18/02/2027", championship: true));
        await db.SaveChangesAsync();

        // Кэш ещё жив (в бою он сбрасывается целиком после импорта).
        Assert.Equal(2025, await provider.StartYearAtAsync(new DateTime(2027, 3, 1)));
    }

    /// <summary>Сезон открыт (зимний чемпионат позади) — объяснять нечего, заметки нет.</summary>
    [Fact]
    public async Task PendingNotice_IsNull_WhenSeasonIsOpen()
    {
        await using var db = CreateDb(nameof(PendingNotice_IsNull_WhenSeasonIsOpen));
        db.Add(Comp("Winter 2025/26", "26/02/2026", championship: true));
        await db.SaveChangesAsync();

        var provider = new ShowcaseSeasonProvider(db, new NullCache());

        Assert.Null(await provider.PendingNoticeAtAsync(new DateTime(2026, 3, 1)));
    }

    /// <summary>
    /// Сентябрь: новый сезон идёт по календарю, витрина держит прошлый — заметка называет
    /// ОБА сезона. Без неё пустая карточка выглядит как поломка, а не как «ещё не начался».
    /// </summary>
    [Fact]
    public async Task PendingNotice_NamesBothSeasons()
    {
        await using var db = CreateDb(nameof(PendingNotice_NamesBothSeasons));
        db.Add(Comp("Winter 2025/26", "26/02/2026", championship: true));
        await db.SaveChangesAsync();

        var provider = new ShowcaseSeasonProvider(db, new NullCache());
        var notice = await provider.PendingNoticeAtAsync(new DateTime(2026, 9, 1));

        Assert.NotNull(notice);
        Assert.Equal(2025, notice!.ShowingSeason);
        Assert.Equal("2025/26", notice.ShowingLabel);
        Assert.Equal(2026, notice.PendingSeason);
        Assert.Equal("2026/27", notice.PendingLabel);
        // Расписания нового сезона в базе нет — даты не обещаем.
        Assert.Null(notice.WinterStarts);
    }

    /// <summary>
    /// Расписание нового сезона уже затянуто — заметка называет БЛИЖАЙШИЙ зимний старт.
    /// Это не дата переключения (её задаёт последний чемпионат всех ступеней), поэтому
    /// берётся именно первый из будущих, а не последний.
    /// </summary>
    [Fact]
    public async Task PendingNotice_TellsWhenWinterChampionshipsStart()
    {
        await using var db = CreateDb(nameof(PendingNotice_TellsWhenWinterChampionshipsStart));
        db.AddRange(
            Comp("Winter 2025/26", "26/02/2026", championship: true),
            Comp("Masters winter 2026/27", "18/02/2027", championship: true),
            Comp("Age winter 2026/27", "25/02/2027", championship: true));
        await db.SaveChangesAsync();

        var provider = new ShowcaseSeasonProvider(db, new NullCache());
        var notice = await provider.PendingNoticeAtAsync(new DateTime(2026, 9, 1));

        Assert.NotNull(notice);
        Assert.Equal("18/02/2027", notice!.WinterStarts);
    }

    /// <summary>
    /// Чемпионат ждущего сезона уже проплыт, но сезон ещё не закрыт последней ступенью —
    /// обещать прошедшую дату нельзя, она читалась бы как «уже должно было открыться».
    /// </summary>
    [Fact]
    public async Task PendingNotice_DoesNotPromisePastDates()
    {
        await using var db = CreateDb(nameof(PendingNotice_DoesNotPromisePastDates));
        db.AddRange(
            Comp("Winter 2025/26", "26/02/2026", championship: true),
            Comp("Masters winter 2026/27", "10/01/2027", championship: true),
            Comp("Age winter 2026/27", "25/02/2027", championship: true));
        await db.SaveChangesAsync();

        var provider = new ShowcaseSeasonProvider(db, new NullCache());
        var notice = await provider.PendingNoticeAtAsync(new DateTime(2027, 1, 20));

        Assert.NotNull(notice);
        Assert.Equal("25/02/2027", notice!.WinterStarts);
    }
}
