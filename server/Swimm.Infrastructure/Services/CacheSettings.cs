namespace Swimm.Infrastructure.Services;

/// <summary>
/// Настройки кэша на /Admin/Settings (docs/plans/cache-row-precision-plan.md §2.5, §4-6).
/// Смена любой настройки сбрасывает весь кэш (<c>AdminController.UpdateSetting</c>) — смеси
/// записей, собранных при старом и новом положении, не бывает.
/// </summary>
public static class CacheSettings
{
    /// <summary>
    /// Выключатель сужения до строк (bool, дефолт — <see cref="DefaultRowPrecision"/>): выключен —
    /// блоки <c>CacheRows</c> ничего не делают, записи зависят от таблиц. Аварийный рычаг: всплыл
    /// недосброс — выключить за секунду и чинить спокойно, без отката релиза.
    /// </summary>
    public const string RowPrecision = "CacheRowPrecision";

    /// <summary>
    /// Сужение включено по умолчанию — с приёмки К4б.5 (страницы группы и клуба), решение Влада §8-5.
    /// </summary>
    public const bool DefaultRowPrecision = true;

    /// <summary>
    /// Выключатель точности по служебным колонкам (bool, дефолт — <see cref="DefaultColumnPrecision"/>),
    /// К4б.6: включён — правка только служебных колонок (<c>CacheServiceColumns</c>) сбрасывает
    /// <c>col:T.C</c> вместо <c>table:T</c>; выключен — таблицу, как обычная правка. Действует на
    /// запись: читатели метки <c>col:</c> носят при любом положении, так что смена положения не
    /// оставит в кэше записи без нужной метки. Аварийный рычаг, как и <see cref="RowPrecision"/>.
    /// </summary>
    public const string ColumnPrecision = "CacheColumnPrecision";

    /// <summary>
    /// Включена по умолчанию — с приёмки К4б.6 (служебные колонки), решение Влада 14.09.2026, как
    /// у <see cref="DefaultRowPrecision"/> после К4б.5.
    /// </summary>
    public const bool DefaultColumnPrecision = true;

    /// <summary>
    /// Сверка на попадании, % (int 0–100): попадание в запись, суженную до строк (или читавшую
    /// служебные колонки, пока включён <see cref="ColumnPrecision"/>), с этой вероятностью строится
    /// заново мимо кэша и сравнивается. В Development 20, на проде 0.
    /// </summary>
    public const string HitVerifyPercent = "CacheHitVerifyPercent";

    /// <summary>Доля сверки по умолчанию: в Development ощутимая, на проде выключена (решение §8-4).</summary>
    public static int DefaultHitVerifyPercent(bool development) => development ? 20 : 0;
}
