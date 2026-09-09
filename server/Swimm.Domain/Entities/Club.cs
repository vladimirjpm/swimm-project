using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Swimm.Domain.Entities;

/// <summary>
/// Справочник клубов.
/// </summary>
[Index(nameof(Name))]
public class Club
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    /// <summary>Название клуба (иврит/кириллица)</summary>
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Название клуба (англ.)</summary>
    [MaxLength(200)]
    public string NameEn { get; set; } = string.Empty;

    /// <summary>
    /// Псевдоклуб: в графе клуба протокола стоит страна/сборная («USA», «Israel»,
    /// «M25», «Maccabiah MIX»), а не реальный клуб. Такие записи исключаются из
    /// club-summary и дедупа; страна уходит в <see cref="CountryId"/>.
    /// </summary>
    public bool IsPseudo { get; set; }

    /// <summary>
    /// Обложка страницы клуба (URL). Задаётся в /Admin/Clubs/Edit и в табе Admin страницы.
    ///
    /// Импорт клубы только СОЗДАЁТ (JsonImportService: `new Club{…}` при промахе поиска) и
    /// существующие строки не переписывает — переимпорт картинку не сотрёт.
    /// </summary>
    [MaxLength(1000)]
    public string? CoverImageUrl { get; set; }

    /// <summary>
    /// Настройки ОТОБРАЖЕНИЯ страницы (JSON) — разбирает <see cref="EntityDisplaySettings"/>,
    /// руками колонку не читать. Пусто = дефолты.
    /// </summary>
    [Column(TypeName = "jsonb")]
    public string? DisplaySettings { get; set; }

    /// <summary>Ссылка на страну (опционально)</summary>
    public int? CountryId { get; set; }

    [ForeignKey(nameof(CountryId))]
    public Country? Country { get; set; }

    /// <summary>
    /// Клуб склеен в другой (merge дублей): здесь Id клуба-приёмника, null — клуб живой.
    ///
    /// Merge не удаляет строку, а помечает её — иначе ссылки на клуб гниют после каждой
    /// чистки (клубов было 231, после склейки 128), а страница <c>/clubs/{старый id}</c>
    /// отдавала бы 404. Вместо этого она молча показывает клуб-приёмник; редиректа нет
    /// (решение Влада 2026-08-01).
    ///
    /// Все связи (результаты, пловцы, hub-группы, избранное) при merge физически
    /// переезжают на приёмника, поэтому склеенный клуб пуст и из публичных выборок
    /// и из дедупа исключается по этому полю.
    /// </summary>
    public int? MergedIntoId { get; set; }

    [ForeignKey(nameof(MergedIntoId))]
    public Club? MergedInto { get; set; }
}
