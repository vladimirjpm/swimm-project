namespace Swimm.Application.Dtos;

/// <summary>Одна отладочная опция для админки.</summary>
/// <param name="Enabled">Галочка самой опции.</param>
/// <param name="Effective">
/// Действует ли она сейчас на деле: <c>Enabled</c> И общий тумблер <c>DebugDetails</c>.
/// Именно это значение видит сайт, и именно его стоит показывать админу — иначе включённая
/// галочка при выключенном общем тумблере читается как «работает», а она не работает.
/// </param>
public sealed record DebugOptionDto(
    string Key,
    string Title,
    string Description,
    bool Enabled,
    bool Effective,
    DateTime UpdatedAt,
    string? UpdatedBy);

/// <summary>Состояние всей подсистемы отладочных подробностей.</summary>
public sealed record DebugOptionsDto(
    bool MasterEnabled,
    IReadOnlyList<DebugOptionDto> Options);
