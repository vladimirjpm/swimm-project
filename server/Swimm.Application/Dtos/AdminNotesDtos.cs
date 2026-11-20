namespace Swimm.Application.Dtos;

/// <summary>
/// Справка админки в списке (docs/admin-pages/admin-notes.md): ключ = имя файла без <c>.md</c>
/// в <c>docs/admin-notes/</c>, заголовок из первой строки <c># …</c>, дата из строки
/// «Проверено: ДД.ММ.ГГГГ» (null — строки нет или дата не разобралась), путь от корня репозитория.
/// </summary>
public sealed record AdminNoteInfo(string Key, string Title, DateOnly? Checked, string Path);

/// <summary>Одна справка для попапа: то же, что в списке, плюс тело, отрисованное в HTML (без заголовка и даты).</summary>
public sealed record AdminNoteView(string Key, string Title, DateOnly? Checked, string Path, string Html);
