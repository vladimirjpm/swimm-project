namespace Swimm.Infrastructure.Services.Email;

/// <summary>
/// Адрес почты для логов уровня Warning и выше: они уходят в Application Insights
/// (docs/plans/application-insights-plan.md, А2-бис), а email — личные данные.
/// Остаются первая буква и домен: видно, что письма не доходят именно на gmail.com, а не всем,
/// и по одной строке лога человека не найти.
/// </summary>
internal static class EmailLogMask
{
    public static string Mask(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return "(empty)";
        var at = email.LastIndexOf('@');
        if (at <= 0 || at == email.Length - 1) return "***";
        return email[0] + "***" + email[at..];
    }
}
