using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Swimm.Domain.Entities;

/// <summary>
/// Игрок loglig.com, встреченный на странице дисциплины: id из ссылки <c>/Players/Details/{id}</c>
/// и имя, как оно там напечатано (Р122, И-69). Нужен одному: чтобы находка «два игрока loglig на
/// одной карточке» (<c>swimmers.two-loglig-players</c>) называла обоих по имени — у второго
/// игрока нашей карточки нет, и взять имя больше неоткуда (<c>Results.LogligPlayerId</c> хранит
/// только id).
///
/// Пишет шаг после импорта (<c>LogligRowPlayerService</c>), последним увиденным написанием.
/// Отдельная Sys_-таблица: справочник чужого сайта для админки, не данные витрины. БЕЗ grant
/// swimm_ro, не в seed-tables.txt (на копии с прода находка покажет id без имени — до первого
/// прогона шага), FK нет.
/// </summary>
public class LogligPlayer
{
    /// <summary>Id игрока на loglig — он же ключ.</summary>
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public int LogligId { get; set; }

    /// <summary>Имя одной строкой, как на странице дисциплины («имя фамилия»).</summary>
    [Required, MaxLength(200)]
    public string FullName { get; set; } = string.Empty;

    /// <summary>Год рождения из соседней колонки; null — страница его не напечатала.</summary>
    public int? BirthYear { get; set; }

    /// <summary>Когда видели в последний раз (UTC).</summary>
    public DateTime SeenAt { get; set; }
}
