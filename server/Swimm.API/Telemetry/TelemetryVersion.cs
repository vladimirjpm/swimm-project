using System.Reflection;

namespace Swimm.API.Telemetry;

/// <summary>
/// Версия сборки для телеметрии (<c>service.version</c> → колонка <c>AppVersion</c> в Azure): короткий
/// хэш коммита. Деплой собирает с <c>-p:InformationalVersion=&lt;sha&gt;</c>, SDK дописывает «+sha»,
/// отсюда вид «1.0.0+abc…» или «abc…+abc…» — берём последний кусок после «+», первые 8 знаков.
/// </summary>
internal static class TelemetryVersion
{
    internal static readonly string Current = Short(
        typeof(TelemetryVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

    internal static string Short(string? informational)
    {
        if (string.IsNullOrWhiteSpace(informational)) return "unknown";
        var last = informational[(informational.LastIndexOf('+') + 1)..];
        return last.Length > 8 ? last[..8] : last;
    }
}
