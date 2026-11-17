using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.WebUtilities;
using Swimm.API.Hosting;

namespace Swimm.API.Security;

/// <summary>
/// Сорвавшийся возврат от Google на <c>/signin-google</c>. Обычные причины — человек нажал «Отмена»
/// на экране Google (<c>access_denied</c>), вернулся кнопкой «назад» на уже отработавший адрес,
/// открыл старую вкладку или истекла транзитная кука (<c>Correlation failed</c>). Без обработчика
/// обработчик Google бросал исключение, и человек видел страницу ошибки 500 (поймано телеметрией
/// 17.11.2026, docs/plans/application-insights-plan.md). Ошибкой сайта это не считаем.
///
/// Что делаем: молча возвращаем на страницу, с которой человек пошёл входить (она лежит в
/// <c>returnUrl</c> адреса возврата из state), через те же <see cref="ReturnUrlRules"/>, что и вход;
/// state нечитаем — на главную. Причину пишет сам обработчик Google строкой уровня Information
/// («Error from RemoteAuthentication»), в Azure она не уходит — и не надо: «Отмена» не авария.
/// </summary>
public static class GoogleRemoteFailure
{
    public static Task HandleAsync(RemoteFailureContext context)
    {
        var http = context.HttpContext;
        var config = http.RequestServices.GetRequiredService<IConfiguration>();
        var env = http.RequestServices.GetRequiredService<IWebHostEnvironment>();
        var baseUrl = config[SeoPages.BaseUrlKey] ?? SeoPages.DefaultBaseUrl;
        var canonicalHost = Uri.TryCreate(baseUrl, UriKind.Absolute, out var b) ? b.Host : null;

        var target = ReturnUrlRules.Safe(
            ReturnUrlFrom(context.Properties?.RedirectUri), http.Request.Host.Host, canonicalHost, env.IsLocalMachine());

        context.Response.Redirect(target);
        context.HandleResponse();
        return Task.CompletedTask;
    }

    /// <summary>
    /// <c>returnUrl</c> из адреса возврата, который ставит <c>AuthController.LoginGoogle</c>
    /// (<c>/auth/google-callback?returnUrl=…</c>). Нет адреса или параметра — null (дальше — главная).
    /// Проверку «свой ли сайт» делает вызывающий через <see cref="ReturnUrlRules"/>.
    /// </summary>
    public static string? ReturnUrlFrom(string? redirectUri)
    {
        if (string.IsNullOrEmpty(redirectUri)) return null;
        var q = redirectUri.IndexOf('?');
        if (q < 0) return null;
        var query = QueryHelpers.ParseQuery(redirectUri[q..]);
        return query.TryGetValue("returnUrl", out var v) && !string.IsNullOrEmpty(v.ToString()) ? v.ToString() : null;
    }
}
