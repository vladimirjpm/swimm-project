using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Справки админки — MD-файлы <c>docs/admin-notes/*.md</c> (docs/admin-pages/admin-notes.md).
/// Значок «ⓘ» на странице ставится по ключу; справки нет — значка нет. Файлы читаются с диска
/// репозитория, как /Admin/Docs: правка файла видна без перезапуска.
/// </summary>
public interface IAdminNotes
{
    /// <summary>Все справки папки по ключу; нет папки (опубликованная сборка) — пустой список.</summary>
    IReadOnlyList<AdminNoteInfo> List();

    /// <summary>Справка по ключу. Ключ ищется только среди реально существующих файлов папки; нет — null.</summary>
    AdminNoteView? Get(string key);
}
