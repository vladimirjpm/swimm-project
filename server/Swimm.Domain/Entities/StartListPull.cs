using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Swimm.Domain.Entities;

/// <summary>Исход одного забора стартового протокола (<see cref="StartListPull.Status"/>).</summary>
public static class StartListPullStatus
{
    /// <summary>Все заплывы соревнования прочитаны.</summary>
    public const string Ok = "ok";

    /// <summary>Часть заплывов не прочиталась (сеть, 404 на отдельной странице).</summary>
    public const string Partial = "partial";

    /// <summary>Программа есть, но стартовых протоколов ещё нет — посев не сделан.
    /// Это НЕ ошибка: за неделю до старта нормальное состояние.</summary>
    public const string Empty = "empty";

    /// <summary>Забор сорвался целиком.</summary>
    public const string Error = "error";
}

/// <summary>
/// Журнал заборов стартового протокола: что и когда приехало
/// (docs/plans/start-list-plan.md §3.1).
///
/// Зачем журнал, а не просто перезапись заявок. Стартовый протокол — источник, который
/// МЕНЯЕТСЯ до последнего дня: снятия, перестановка дорожек, объединение заплывов. Без записи
/// «в этот раз пришло столько, добавилось столько, уехало столько» вопрос «почему у ребёнка
/// вчера была дорожка 5, а сегодня 3» разбирается только глазами. Та же роль, что у
/// <see cref="ImportReconciliation"/> для импорта результатов.
///
/// Sys_-таблица: НАША внутренняя кухня, публичной роли swimm_ro не выдаётся.
/// </summary>
[Index(nameof(OrgCompId), nameof(PulledAt))]
public class StartListPull
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    /// <summary>compID соревнования на isr.org.il — как и у заявки, идентичность идёт по нему
    /// (FK нет: справочной строки может ещё не существовать).</summary>
    public int OrgCompId { get; set; }

    public DateTime PulledAt { get; set; } = DateTime.UtcNow;

    /// <summary>Заплывов в программе соревнования.</summary>
    public int Events { get; set; }

    /// <summary>Строк заявок прочитано из источника.</summary>
    public int Entries { get; set; }

    /// <summary>Новых строк относительно прошлого забора.</summary>
    public int Added { get; set; }

    /// <summary>Строк, исчезнувших с прошлого забора, — снялись до старта.</summary>
    public int Removed { get; set; }

    /// <summary>Строк, у которых сменился заплыв или дорожка, — пересев.</summary>
    public int Moved { get; set; }

    /// <summary>ok | partial | empty | error (см. <see cref="StartListPullStatus"/>).</summary>
    [MaxLength(20)]
    public string Status { get; set; } = StartListPullStatus.Ok;

    /// <summary>Текст сбоя — для диагностики в админке; null у успешного забора.</summary>
    [MaxLength(1000)]
    public string? Error { get; set; }
}
