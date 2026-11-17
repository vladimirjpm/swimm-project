using Swimm.API.Hosting;

namespace Swimm.API.Telemetry;

/// <summary>
/// Имя роли (<c>service.name</c> → «Cloud role name» в Application Insights): по нему страница
/// <c>/Admin/AppInsights</c> и оповещения отличают сайт от локальной админки (план А2.3).
/// Явный ключ <see cref="RoleNameKey"/> важнее окружения — на случай второго инстанса или проверки.
/// </summary>
internal static class TelemetryRoleName
{
    public const string RoleNameKey = "Telemetry:RoleName";

    public const string Web = "swimhub-web";
    public const string ProdAdmin = "swimhub-prodadmin";
    public const string Dev = "swimhub-dev";

    public static string Resolve(IConfiguration config, IHostEnvironment env)
    {
        var explicitName = config[RoleNameKey];
        if (!string.IsNullOrWhiteSpace(explicitName)) return explicitName.Trim();
        if (env.IsProdAdmin()) return ProdAdmin;
        if (env.IsDevelopment()) return Dev;
        return Web;
    }
}
