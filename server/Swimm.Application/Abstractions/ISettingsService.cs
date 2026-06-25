using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

public interface ISettingsService
{
    IReadOnlyList<AdminSetting> GetAll();
    AdminSetting? Get(string key);
    T GetValue<T>(string key, T fallback);
    bool Update(string key, string newValue);
}
