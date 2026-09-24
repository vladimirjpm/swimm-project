using Microsoft.EntityFrameworkCore;
using Swimm.Application.Mapping;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Видит ли пользователь тестовые группы (<c>HubGroup.IsTest</c>) — по его email в базе.
/// Правило — <see cref="TestAccountRules"/>; здесь только чтение email. Зовётся ТОЛЬКО когда
/// группа тестовая, поэтому на обычных группах лишнего запроса нет.
/// </summary>
internal static class TestGroupAccess
{
    public static async Task<bool> CanSeeAsync(SwimmDbContext db, int? userId, bool isSiteAdmin)
    {
        if (isSiteAdmin) return true;
        if (userId is not int uid) return false;

        var email = await db.AppUsers.AsNoTracking()
            .Where(u => u.Id == uid)
            .Select(u => u.Email)
            .FirstOrDefaultAsync();
        return TestAccountRules.IsTestEmail(email);
    }
}
