namespace Swimm.Domain.Entities;

/// <summary>
/// Значение настройки из /Admin/Settings, изменённое админом (решение В7,
/// docs/plans/azure-deploy-plan.md §10). Одна строка — одна ПЕРЕОПРЕДЕЛЁННАЯ настройка: реестр
/// ключей, их тип, описание и значения по умолчанию остаются в коде (<c>AdminSettingsService</c>),
/// здесь только то, что поменяли. Нет строки — действует дефолт.
///
/// Зачем в БД. Раньше настройки жили в памяти процесса: деплой сбрасывал их, а режим обслуживания
/// снимался сам. С В17 админка работает в ДРУГОМ процессе (локально, ProdAdmin) — включённый ею
/// режим обслуживания прод видит только через общую БД (и мост кэша, который велит перечитать).
/// </summary>
public class AdminSettingValue
{
    /// <summary>Ключ настройки — он же первичный ключ.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Значение строкой, как в реестре (bool/int/string).</summary>
    public string Value { get; set; } = string.Empty;

    public DateTime UpdatedAt { get; set; }

    /// <summary>Кто последний менял — как в остальных админ-сущностях.</summary>
    public string? UpdatedBy { get; set; }
}
