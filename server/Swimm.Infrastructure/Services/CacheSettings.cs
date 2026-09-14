namespace Swimm.Infrastructure.Services;

/// <summary>
/// Настройки кэша на /Admin/Settings (docs/plans/cache-row-precision-plan.md §2.5, §4-6).
/// Смена любой настройки сбрасывает весь кэш (<c>AdminController.UpdateSetting</c>) — смеси
/// записей, собранных при старом и новом положении, не бывает.
/// </summary>
public static class CacheSettings
{
    /// <summary>
    /// Выключатель сужения до строк (bool, дефолт false): выключен — блоки <c>CacheRows</c> ничего не
    /// делают, записи зависят от таблиц. Аварийный рычаг: всплыл недосброс — выключить за секунду и
    /// чинить спокойно, без отката релиза. Включается после приёмки К4б.4.
    /// </summary>
    public const string RowPrecision = "CacheRowPrecision";

    /// <summary>
    /// Сверка на попадании, % (int 0–100): попадание в запись, суженную до строк, с этой
    /// вероятностью строится заново мимо кэша и сравнивается. В Development 20, на проде 0.
    /// </summary>
    public const string HitVerifyPercent = "CacheHitVerifyPercent";

    /// <summary>Доля сверки по умолчанию: в Development ощутимая, на проде выключена (решение §8-4).</summary>
    public static int DefaultHitVerifyPercent(bool development) => development ? 20 : 0;
}
