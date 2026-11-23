using System.Net;
using Npgsql;

namespace Swimm.API.Security;

/// <summary>
/// Сторож всего, что существует только для разработки на машине Влада: обход логина
/// (<see cref="DevAdminBypass"/>), переключатель персонажей и ручки <c>/api/dev/*</c>, сидер
/// <c>--seed-personas</c>, кнопка «Сбросить» персонажа в /Admin/Users
/// (docs/plans/test-personas-plan.md, раздел «Локальная админка под utest-admin»).
///
/// Условия процесса — ВСЕ сразу, иначе механизма нет вовсе (не регистрируется):
/// <list type="number">
/// <item>окружение Development (ProdAdmin и Production — другие имена окружения);</item>
/// <item>каждая строка подключения, через которую приложение читает и пишет
///   (<see cref="AppConnectionNames"/>), смотрит на localhost — тот же признак «подключены к
///   проду», что у сторожа ProdAdmin (<c>ProdAdminGuard.IsLocalHost</c>), только строже:
///   нечитаемая строка считается НЕ локальной.</item>
/// </list>
/// Условие запроса — <see cref="IsLoopbackRequest"/>: соединение с loopback и без прокси-заголовков.
/// </summary>
public static class DevLocalGate
{
    /// <summary>
    /// Строки подключения, которые реально использует приложение (DependencyInjection: Default,
    /// Admin, Read; Migration — у миграций и дизайн-фабрики). <c>ProdVersionConnection</c> сюда
    /// намеренно НЕ входит: в Development она смотрит в прод, но только читает номер версии данных
    /// для полосы «локальная база отстаёт» (DataSyncCheckBackgroundService).
    /// </summary>
    public static readonly IReadOnlyList<string> AppConnectionNames =
        ["DefaultConnection", "AdminConnection", "ReadConnection", "MigrationConnection"];

    /// <summary>null — dev-механизмы разрешены; иначе — почему нет (для лога и CLI).</summary>
    public static string? WhyDisabled(IHostEnvironment env, IConfiguration config)
    {
        if (!env.IsDevelopment())
            return $"окружение {env.EnvironmentName}, а не Development";

        foreach (var name in AppConnectionNames)
        {
            var cs = config.GetConnectionString(name);
            if (string.IsNullOrWhiteSpace(cs)) continue; // не задана — берётся DefaultConnection, она проверена
            if (!IsLocalConnectionString(cs))
                return $"строка подключения {name} смотрит не на localhost (прод-база или чужой сервер)";
        }

        if (string.IsNullOrWhiteSpace(config.GetConnectionString("DefaultConnection")))
            return "нет строки подключения DefaultConnection";

        return null;
    }

    /// <summary>Все хосты строки — loopback. Битая строка или пустой хост — НЕ локальная.</summary>
    internal static bool IsLocalConnectionString(string connectionString)
    {
        string? hosts;
        try { hosts = new NpgsqlConnectionStringBuilder(connectionString).Host; }
        catch (ArgumentException) { return false; }
        if (string.IsNullOrWhiteSpace(hosts)) return false;

        // Npgsql принимает список хостов через запятую (с портом host:port) — локальными должны быть все.
        return hosts.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .All(h => IsLoopbackHost(StripPort(h)));
    }

    private static string StripPort(string host)
    {
        if (host.StartsWith('['))
        {
            var close = host.IndexOf(']');
            return close > 0 ? host[1..close] : host;
        }
        var colon = host.LastIndexOf(':');
        // Одно двоеточие — host:port; несколько — голый IPv6 (::1).
        return colon > 0 && host.IndexOf(':') == colon ? host[..colon] : host;
    }

    private static bool IsLoopbackHost(string host) =>
        host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
        || (IPAddress.TryParse(host, out var ip)
            && IPAddress.IsLoopback(ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip));

    /// <summary>
    /// Запрос пришёл с этой же машины: адрес соединения loopback и НИКАКИХ прокси-заголовков.
    /// Заголовки проверяются потому, что <c>UseForwardedHeaders</c> в Program.cs доверяет любому
    /// источнику (KnownProxies очищены — так надо за Azure): без этой проверки запрос из сети с
    /// <c>X-Forwarded-For: 127.0.0.1</c> выглядел бы локальным. Применённый заголовок middleware
    /// переносит в <c>X-Original-For</c> — его тоже ловим.
    /// </summary>
    public static bool IsLoopbackRequest(HttpContext context)
    {
        var ip = context.Connection.RemoteIpAddress;
        if (ip is null) return false;
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (!IPAddress.IsLoopback(ip)) return false;

        var h = context.Request.Headers;
        return !h.ContainsKey("X-Forwarded-For")
            && !h.ContainsKey("X-Original-For")
            && !h.ContainsKey("Forwarded");
    }
}
