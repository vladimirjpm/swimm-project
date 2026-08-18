using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Swimm.Domain.Entities;

/// <summary>
/// Публичное примечание к соревнованию — то, что мы объясняем ЧИТАТЕЛЮ сайта.
///
/// Первый (и пока единственный) вид — <see cref="CompetitionNoteKinds.ClubPointsMismatch"/>:
/// почему официальный клубный зачёт неверен, а наш верен. Бейдж «Differs from official» без
/// объяснения — утверждение без доказательства, и у каждого соревнования оно своё.
///
/// Устройство: сама заметка хранит ЯЗЫКОНЕЗАВИСИМОЕ (факты — <see cref="ScaleDiffJson"/>),
/// а тексты лежат строками в <see cref="Texts"/> по языку. Цифры не переводятся, поэтому
/// дублировать их в каждом переводе незачем — и они не разъедутся между языками.
///
/// НЕ <c>Sys_</c>-таблица: текст читает публичная витрина, значит нужен grant для
/// <c>swimm_ro</c> (выдан в миграции).
/// </summary>
[Index(nameof(CompetitionId), nameof(Kind), IsUnique = true)]
public class CompetitionNote
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int CompetitionId { get; set; }

    [ForeignKey(nameof(CompetitionId))]
    public Competition Competition { get; set; } = null!;

    /// <summary>Вид примечания — см. <see cref="CompetitionNoteKinds"/>.</summary>
    [MaxLength(40)]
    public string Kind { get; set; } = string.Empty;

    /// <summary>
    /// Табличка расхождения в JSON: <c>[{"place":21,"expected":5,"actual":6}, …]</c> —
    /// место, сколько по регламенту, сколько начислили официально. null — расхождение
    /// объясняется только прозой.
    ///
    /// Данными, а не свёрстанной таблицей: HTML из базы не знает про тёмную тему и про
    /// телефон, а по этим цифрам компонент рисует таблицу сам — и на любом языке одинаково.
    /// </summary>
    public string? ScaleDiffJson { get; set; }

    /// <summary>
    /// Ссылка на источник, который доказывает объяснение — обычно регламент соревнования
    /// («תקנון», PDF на loglig). Одна на все языки: URL не переводится, как и цифры.
    ///
    /// Только <c>http</c>/<c>https</c> — проверяется при сохранении и ещё раз при выводе:
    /// ссылка попадает в <c>href</c> на публичной странице, а <c>javascript:</c> в href это
    /// готовый XSS.
    /// </summary>
    [MaxLength(1000)]
    public string? SourceUrl { get; set; }

    /// <summary>Тексты по языкам (en/ru/he). Пустой список — заметка ещё не написана.</summary>
    public ICollection<CompetitionNoteText> Texts { get; set; } = [];

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [MaxLength(200)]
    public string? UpdatedBy { get; set; }
}

/// <summary>Перевод примечания. Языки те же, что у попапа-объяснялки: en / ru / he.</summary>
[Index(nameof(NoteId), nameof(Lang), IsUnique = true)]
public class CompetitionNoteText
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int NoteId { get; set; }

    [ForeignKey(nameof(NoteId))]
    public CompetitionNote Note { get; set; } = null!;

    /// <summary>Код языка: <c>en</c> | <c>ru</c> | <c>he</c>.</summary>
    [MaxLength(5)]
    public string Lang { get; set; } = string.Empty;

    /// <summary>Проза объяснения. Абзацы разделяются пустой строкой — как в InfoPopup.</summary>
    public string Body { get; set; } = string.Empty;
}

/// <summary>Виды публичных примечаний к соревнованию.</summary>
public static class CompetitionNoteKinds
{
    /// <summary>Почему официальный клубный зачёт неверен (парен к <c>ClubPointsVerifiedKind = mismatch</c>).</summary>
    public const string ClubPointsMismatch = "club-points-mismatch";

    public static bool IsKnown(string? kind) => kind == ClubPointsMismatch;
}

/// <summary>Языки публичных примечаний — те же три, что у попапа-объяснялки на клиенте.</summary>
public static class CompetitionNoteLangs
{
    public const string En = "en";
    public const string Ru = "ru";
    public const string He = "he";

    public static readonly string[] All = [En, Ru, He];

    public static bool IsKnown(string? lang) => lang is En or Ru or He;
}
