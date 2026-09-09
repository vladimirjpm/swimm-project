using System.Text.Json;
using System.Text.Json.Serialization;

namespace Swimm.Domain;

/// <summary>
/// Настройки ОТОБРАЖЕНИЯ коллектива (клуб или группа) — то, чем управляет владелец страницы,
/// а не то, что приносят данные. Лежат JSON-колонкой на самой сущности
/// (<c>Clubs.DisplaySettings</c>, <c>HubGroups.DisplaySettings</c>), каталог ключей — здесь,
/// в коде. План и обоснование выбора — <c>docs/plans/entity-page-shell-plan.md</c> §3.9.
///
/// Почему колонкой, а не таблицей настроек: новые ключи добавляются без миграции, значения
/// приезжают одним запросом вместе с сущностью (ни джойна, ни второго обращения), и не нужен
/// новый грант — <c>Clubs</c> и <c>HubGroups</c> уже читает <c>swimm_ro</c>, а любая
/// <c>Sys_</c>-таблица потребовала бы второго исключения в <c>02-grants.sql</c> после
/// <c>Sys_RecordIssues</c>. Прецедент в проекте есть: <c>HubGroups.Links</c> — тоже JSON.
///
/// Цена решения — значение нетипизировано в БД; типизацию держит этот класс, и разбирать
/// колонку руками где-то ещё нельзя.
///
/// ⚠ Настройки — это НЕ данные сущности. Картинка шапки (URL) живёт колонкой
/// <c>CoverImageUrl</c>, здесь только «показывать ли» и «взять ли её из медиа».
/// </summary>
public sealed class EntityDisplaySettings
{
    [JsonPropertyName("hero")]
    public HeroDisplaySettings Hero { get; set; } = new();

    private static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Разбор колонки. Пусто, мусор или чужая форма — ДЕФОЛТЫ, а не исключение: настройки
    /// отображения не то, ради чего страница должна падать пятисоткой.
    /// </summary>
    public static EntityDisplaySettings Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new EntityDisplaySettings();
        try
        {
            return JsonSerializer.Deserialize<EntityDisplaySettings>(json, Options)
                   ?? new EntityDisplaySettings();
        }
        catch (JsonException)
        {
            return new EntityDisplaySettings();
        }
    }

    public string ToJson() => JsonSerializer.Serialize(this, Options);
}

/// <summary>Фото шапки: показывать ли и откуда брать.</summary>
public sealed class HeroDisplaySettings
{
    /// <summary>
    /// Показывать блок фото. Дефолт — да: пустая правая колонка схлопывается заглушкой,
    /// а не отсутствием (решение Влада 09.09.2026). Выключатель нужен тем, кому заглушка
    /// мешает и картинки не будет.
    /// </summary>
    [JsonPropertyName("show")]
    public bool Show { get; set; } = true;

    /// <summary>
    /// «Взять фото из медиа-ленты сущности»: id строки медиа. null — берём
    /// <c>CoverImageUrl</c>. Указатель, а не флаг <c>IsHero</c> на самой строке медиа:
    /// так уникальность «одна шапка на сущность» получается бесплатно, медиа-таблицы не
    /// трогаются, и правило одинаково для любой будущей сущности (план §3.9).
    ///
    /// ⚠ У клуба медиа-ленты пока не существует вовсе, поэтому форма для него мертва —
    /// но модель от этого не меняется.
    /// </summary>
    [JsonPropertyName("mediaId")]
    public int? MediaId { get; set; }
}
