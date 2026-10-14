using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Swimm.Application.Abstractions;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>Где лежат изменённые значения настроек (В7). Отдельно от реестра — тесты подставляют свою.</summary>
public interface IAdminSettingsStore
{
    IReadOnlyDictionary<string, string> LoadAll();
    void Save(string key, string value);
}

/// <summary>
/// Хранилище в <c>Sys_AdminSettings</c>. Синхронное: <see cref="ISettingsService"/> синхронный,
/// а запросы тут — одна строка или вся крошечная таблица. Синглтон — поэтому контекст берётся из
/// своего scope на каждый вызов.
///
/// Запись идёт через EF-контекст записи: перехватчик сбрасывает метку таблицы, а мост кэша
/// разносит её по другим процессам — по ней они и перечитывают настройки.
/// </summary>
public sealed class DbAdminSettingsStore(IServiceScopeFactory scopes) : IAdminSettingsStore
{
    public IReadOnlyDictionary<string, string> LoadAll()
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SwimmDbContext>();
        return db.AdminSettingValues.AsNoTracking()
            .ToDictionary(s => s.Key, s => s.Value, StringComparer.Ordinal);
    }

    public void Save(string key, string value)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SwimmDbContext>();
        var actor = scope.ServiceProvider.GetService<ICurrentActor>()?.Name;

        var row = db.AdminSettingValues.Find(key);
        if (row is null)
        {
            row = new AdminSettingValue { Key = key };
            db.AdminSettingValues.Add(row);
        }
        row.Value = value;
        row.UpdatedAt = DateTime.UtcNow;
        row.UpdatedBy = actor is { Length: > 200 } ? actor[..200] : actor;
        db.SaveChanges();
    }
}
