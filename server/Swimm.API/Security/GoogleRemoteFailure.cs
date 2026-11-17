using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.WebUtilities;
using Swimm.API.Hosting;

namespace Swimm.API.Security;

/// <summary>
/// Сорвавшийся возврат от Google на <c>/signin-google</c>. Без обработчика обработчик Google бросал
/// исключение, и человек видел страницу ошибки 500 (поймано телеметрией 17.11.2026,
/// docs/plans/application-insights-plan.md). Человеку в любом случае — редирект на страницу, с
/// которой он пошёл входить (<c>returnUrl</c> из адреса возврата в state, через те же
/// <see cref="ReturnUrlRules"/>, что и вход); state нечитаем — на главную.
///
/// Причины две породы, и в журнал они идут по-разному:
/// <list type="bullet">
/// <item><b>Сделал человек</b> (<see cref="IsUserCaused"/>): «Отмена» на экране Google
///   (<c>error=access_denied</c>), «назад» на отработавший адрес или старая вкладка (нет куки
///   корреляции), мусорный/пустой запрос (нет state — боты). Не авария: в журнале только строка
///   Information от самого обработчика Google, в Azure она не уходит.</item>
/// <item><b>Всё остальное — поломка входа</b>: Google не обменял код на токен (протух или сменён
///   секрет), не ответил userinfo, вернул другую ошибку. Раньше это было видно как 500; чтобы
///   тихий редирект не спрятал «вход через Google сломан у всех», пишем Warning С ИСКЛЮЧЕНИЕМ — оно
///   уходит в Azure в <c>AppExceptions</c> и видно на /Admin/AppInsights («Частые исключения»).</item>
/// </list>
/// Сверка по тексту ошибки хрупка между версиями ASP.NET, но ошибается в безопасную сторону: если
/// текст сменится, причина станет «поломкой» и попадёт в Azure, а не пропадёт (тесты держат текущие
/// тексты). Остаточная слепая зона: если сломается сама кука корреляции у всех (настройки куки за
/// прокси), каждый вход будет «старой вкладкой» — заметно только по жалобам и по Information-логу.
/// </summary>
public static class GoogleRemoteFailure
{
    /// <summary>Категория журнала — по ней же фильтровать в Azure (<c>Properties.CategoryName</c>).</summary>
    public const string LogCategory = "Swimm.API.Security.GoogleRemoteFailure";

    private const string CorrelationFailed = "Correlation failed.";
    private const string StateMissingOrInvalid = "The oauth state was missing or invalid.";

    public static Task HandleAsync(RemoteFailureContext context)
    {
        var http = context.HttpContext;

        if (!IsUserCaused(context.Failure, http.Request.Query["error"]))
        {
            var logger = http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(LogCategory);
            logger.LogWarning(context.Failure,
                "Вход через Google сорвался не по вине человека (поломка входа?): {Reason}", context.Failure?.Message);
        }

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
    /// Сорвал ли вход сам человек (или бот), а не поломка. <paramref name="errorParam"/> — параметр
    /// <c>error</c> из адреса возврата Google.
    /// </summary>
    public static bool IsUserCaused(Exception? failure, string? errorParam)
    {
        if (string.Equals(errorParam, "access_denied", StringComparison.Ordinal)) return true;
        var message = failure?.Message;
        return message is CorrelationFailed or StateMissingOrInvalid;
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
