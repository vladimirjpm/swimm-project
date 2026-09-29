using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Repositories;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Р65 (docs/data-integrity.md): «группа только следит». Роль coach/captain в составе — заявление
/// владельца о чужом человеке, наружу её нет: общий (кэшируемый) ответ страницы группы отдаёт всех
/// как member — и в составе, и в зачёте. Порядок «тренер первым» (решение 26.09.2026) остаётся.
/// </summary>
public class HubGroupPublicRolesTests
{
    private static SwimmReadDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmReadDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private sealed class SettingsStub : ISettingsService
    {
        public IReadOnlyList<AdminSetting> GetAll() => [];
        public AdminSetting? Get(string key) => null;
        public T GetValue<T>(string key, T fallback) => fallback;
        public bool Update(string key, string newValue) => true;
    }

    [Fact]
    public async Task PublicPage_HidesRoles_KeepsCoachFirst()
    {
        await using var db = CreateDb(nameof(PublicPage_HidesRoles_KeepsCoachFirst));
        Swimmer S(string last) => new() { LastName = last, FirstName = "F", LastNameEn = last, FirstNameEn = "F", BirthYear = 1980 };
        var group = new HubGroup
        {
            Name = "Masters", Slug = "masters",
            Owner = new AppUser { Email = "owner@example.com", DisplayName = "Owner", SecurityStamp = "s" }
        };
        var (member, captain, coach) = (S("Member"), S("Captain"), S("Coach"));
        db.AddRange(group, member, captain, coach);
        await db.SaveChangesAsync();
        db.HubGroupMembers.AddRange(
            new HubGroupMember { HubGroupId = group.Id, SwimmerId = member.Id, Role = "member", SortOrder = 1 },
            new HubGroupMember { HubGroupId = group.Id, SwimmerId = captain.Id, Role = "captain", SortOrder = 2 },
            new HubGroupMember { HubGroupId = group.Id, SwimmerId = coach.Id, Role = "coach", SortOrder = 3 });
        await db.SaveChangesAsync();

        var page = await new HubGroupPublicRepository(db, db, new SettingsStub()).GetPageAsync(group.Id, group.Slug);

        Assert.All(page!.Members, m => Assert.Equal("member", m.Role));
        Assert.All(page.Standings, s => Assert.Equal("member", s.Role));
        // Порядок «тренер первым» — без подписи роли.
        Assert.Equal(coach.Id, page.Members[0].SwimmerId);
    }
}
