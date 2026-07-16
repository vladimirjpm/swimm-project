namespace Swimm.Application.Constants;

/// <summary>Канонические типы бассейна. Единственный источник правды для формы и валидации.</summary>
public static class PoolTypes
{
    public const string Short = "25m";
    public const string Long = "50m";
    public static readonly IReadOnlyList<string> All = new[] { Short, Long };
    public static bool IsValid(string? value) => value is not null && All.Contains(value);
}
