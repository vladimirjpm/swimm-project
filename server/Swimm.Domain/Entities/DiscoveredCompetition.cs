using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Swimm.Domain.Entities;

/// <summary>Статусы обнаруженного соревнования во «входящих» автозабора.</summary>
public static class DiscoveredCompetitionStatus
{
    /// <summary>Новое — ещё не затянуто.</summary>
    public const string New = "new";
    /// <summary>Импортировано (вручную или через «затянуть»); матчится и по имени+дате.</summary>
    public const string Imported = "imported";
    /// <summary>Скрыто админом («не интересует»).</summary>
    public const string Ignored = "ignored";
}

/// <summary>
/// Соревнование, обнаруженное автозабором на isr.org.il (фаза 6 роадмапа).
/// Sys_-таблица: приватные «входящие» админки, БЕЗ grant swimm_ro.
/// <see cref="OrgCompId"/> — compID сайта федерации (уникален);
/// <see cref="LogligId"/> — id соревнования в loglig.com (из iframe детальной страницы),
/// по нему строится URL PDF-протокола ExportSwimmingCompetitionResults.
/// </summary>
[Index(nameof(OrgCompId), IsUnique = true)]
public class DiscoveredCompetition
{
    [Key]
    public int Id { get; set; }

    public int OrgCompId { get; set; }

    [MaxLength(300)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Первый день (UTC-полночь).</summary>
    public DateTime DateStart { get; set; }

    /// <summary>Последний день; равен DateStart для однодневных.</summary>
    public DateTime DateEnd { get; set; }

    [MaxLength(300)]
    public string? Venue { get; set; }

    /// <summary>ID соревнования в loglig.com; null — детальная страница ещё не загружалась
    /// или результаты там не опубликованы.</summary>
    public int? LogligId { get; set; }

    /// <summary>new | imported | ignored (см. <see cref="DiscoveredCompetitionStatus"/>).</summary>
    [MaxLength(20)]
    public string Status { get; set; } = DiscoveredCompetitionStatus.New;

    /// <summary>
    /// Вид спорта: swimming | artistic | other (см. <c>Disciplines</c> в Application).
    /// Проставляется догадкой по названию при обнаружении и правится вручную в админке —
    /// поэтому автозабор его НЕ перезаписывает у уже известных строк.
    /// </summary>
    [MaxLength(20)]
    public string Discipline { get; set; } = "swimming";

    /// <summary>Языки, на которых PDF-протокол успешно загружался с loglig:
    /// null (не загружался) | "he" | "en" | "he,en". Заполняется при «затянуть» и
    /// «синхронизировать языки» — по нему админка показывает бэйджи и предлагает досинхронизацию.</summary>
    [MaxLength(20)]
    public string? Languages { get; set; }

    public DateTime DiscoveredAt { get; set; } = DateTime.UtcNow;

    /// <summary>Когда соревнование в последний раз встречалось в списке на сайте.</summary>
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;

    /// <summary>Последняя ошибка забора деталей/PDF — для явной диагностики в админке (B4).</summary>
    [MaxLength(1000)]
    public string? LastError { get; set; }

    /// <summary>
    /// Когда установлено, что у соревнования НЕТ протокола: PDF на isr.org.il пуст (страница
    /// без единой строки текста) либо парсер не нашёл ни одного соревнования.
    ///
    /// Отдельно от <see cref="LastError"/> сознательно: ошибка — про сбой, который стоит
    /// повторить, а пустой источник — про факт «тянуть нечего». Без этой пометки строка
    /// выглядит как обычная «новая», и человек пробует «Затянуть» снова и снова.
    /// null — источник не признан пустым.
    /// </summary>
    public DateTime? EmptySourceAt { get; set; }

    /// <summary>Кто/что поставило пометку: «auto» (разбор) или email админа (вручную).</summary>
    [MaxLength(200)]
    public string? EmptySourceBy { get; set; }
}
