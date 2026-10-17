using System.Net;
using System.Text.RegularExpressions;

namespace Swimm.Infrastructure.Services.Email;

/// <summary>
/// Общий вид писем сайта: шапка с логотипом SwimHub, тело, подвал со ссылкой на сайт. Тела писем
/// по местам остаются короткими HTML-фрагментами (LocalAuthService, заявки групп на клуб, тестовое
/// письмо админки) — обёртку надевает <see cref="SmtpEmailSender"/> в одном месте.
///
/// Вёрстка — почтовая: таблицы и инлайн-стили, без внешнего CSS (Gmail/Outlook его режут).
/// Логотип — вложение с Content-ID (<see cref="LogoContentId"/>), а не ссылка на сайт: так он
/// виден сразу, без «показать картинки», и не зависит от того, какой процесс шлёт письмо
/// (сайт или локальная админка ProdAdmin).
/// </summary>
public static partial class EmailLayout
{
    public const string LogoContentId = "swimhub-logo";

    /// <summary>Логотип 96×96 (рисуется 48×48 — чётко на ретине), вшит в сборку.</summary>
    public static byte[] LogoPng() => LogoBytes.Value;

    private static readonly Lazy<byte[]> LogoBytes = new(() =>
    {
        using var s = typeof(EmailLayout).Assembly.GetManifestResourceStream(LogoResourceName)
            ?? throw new InvalidOperationException($"Нет ресурса {LogoResourceName}");
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    });

    internal const string LogoResourceName = "Swimm.Infrastructure.Email.swimhub-logo-96.png";

    // Цвета бренда: фон логотипа (#011a49) — шапка и кнопка, чтобы квадрат логотипа сливался с полосой.
    private const string Navy = "#011a49";
    private const string Link = "#0b6e8a";
    private const string FontStack = "-apple-system,'Segoe UI',Roboto,Helvetica,Arial,sans-serif";

    /// <summary>
    /// Кнопка-ссылка для писем со ссылкой-действием (подтвердить почту, сбросить пароль). Голую
    /// ссылку под кнопкой вызывающий оставляет сам: кнопки режет часть клиентов.
    /// </summary>
    public static string Button(string url, string label)
    {
        var href = WebUtility.HtmlEncode(url);
        return $"""<table role="presentation" cellpadding="0" cellspacing="0" border="0" style="margin:20px 0"><tr><td style="border-radius:8px;background:{Navy}"><a href="{href}" style="display:inline-block;padding:12px 22px;font-family:{FontStack};font-size:15px;font-weight:600;color:#ffffff;text-decoration:none;border-radius:8px">{WebUtility.HtmlEncode(label)}</a></td></tr></table>""";
    }

    /// <summary>Полное HTML-письмо вокруг фрагмента <paramref name="bodyHtml"/>.</summary>
    public static string Html(string subject, string bodyHtml, string siteUrl)
    {
        var site = WebUtility.HtmlEncode(siteUrl);
        var siteHost = WebUtility.HtmlEncode(SiteHost(siteUrl));
        return $"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width,initial-scale=1">
            <meta name="color-scheme" content="light">
            <title>{WebUtility.HtmlEncode(subject)}</title>
            </head>
            <body style="margin:0;padding:0;background:#eef1f6">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="background:#eef1f6">
            <tr><td align="center" style="padding:24px 12px">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="max-width:560px;background:#ffffff;border-radius:12px;overflow:hidden">
            <tr><td style="background:{Navy};padding:16px 24px">
            <a href="{site}" style="text-decoration:none">
            <img src="cid:{LogoContentId}" width="48" height="48" alt="SwimHub" style="display:inline-block;vertical-align:middle;border:0;border-radius:10px">
            <span style="display:inline-block;vertical-align:middle;margin-left:12px;font-family:{FontStack};font-size:22px;font-weight:700;letter-spacing:.2px;color:#ffffff">SwimHub</span>
            </a>
            </td></tr>
            <tr><td style="padding:28px 24px 24px;font-family:{FontStack};font-size:15px;line-height:1.6;color:#1f2937">
            {StyleLinks(bodyHtml)}
            </td></tr>
            <tr><td style="padding:16px 24px 22px;border-top:1px solid #e5e7eb;font-family:{FontStack};font-size:12px;line-height:1.5;color:#6b7280">
            SwimHub — swimming results in Israel · <a href="{site}" style="color:#6b7280">{siteHost}</a><br>
            This is an automated message, please don't reply.
            </td></tr>
            </table>
            </td></tr>
            </table>
            </body>
            </html>
            """;
    }

    /// <summary>
    /// Текстовая версия того же письма (multipart/alternative): её читают текстовые клиенты, и
    /// письмо без неё спам-фильтры оценивают хуже. Ссылка «текст → адрес» печатается обоими.
    /// </summary>
    public static string PlainText(string bodyHtml, string siteUrl)
    {
        var s = AnchorRx().Replace(bodyHtml, m =>
        {
            var url = WebUtility.HtmlDecode(m.Groups["url"].Value);
            var text = WebUtility.HtmlDecode(TagRx().Replace(m.Groups["text"].Value, "")).Trim();
            return text.Length == 0 || text == url ? url : $"{text}: {url}";
        });
        s = BreakRx().Replace(s, "\n");
        s = BlockEndRx().Replace(s, "\n");
        s = WebUtility.HtmlDecode(TagRx().Replace(s, ""));
        var lines = s.Replace("\r", "").Split('\n').Select(l => l.Trim());
        s = ManyBlankRx().Replace(string.Join("\n", lines), "\n\n").Trim();
        return $"{s}\n\n--\nSwimHub — swimming results in Israel\n{siteUrl}\n";
    }

    // Ссылки в теле — цвет бренда, если вызывающий не задал свой стиль (у кнопки он свой).
    private static string StyleLinks(string html) =>
        UnstyledAnchorRx().Replace(html, $"""<a style="color:{Link}" """);

    private static string SiteHost(string siteUrl) =>
        Uri.TryCreate(siteUrl, UriKind.Absolute, out var u) ? u.Host : siteUrl;

    [GeneratedRegex("""<a\s[^>]*?href="(?<url>[^"]*)"[^>]*>(?<text>.*?)</a>""", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex AnchorRx();

    [GeneratedRegex("""<a\s(?![^>]*\bstyle=)""", RegexOptions.IgnoreCase)]
    private static partial Regex UnstyledAnchorRx();

    [GeneratedRegex("""<br\s*/?>""", RegexOptions.IgnoreCase)]
    private static partial Regex BreakRx();

    [GeneratedRegex("""</(p|div|tr|table|h[1-6]|li)>""", RegexOptions.IgnoreCase)]
    private static partial Regex BlockEndRx();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex TagRx();

    [GeneratedRegex("\n{3,}")]
    private static partial Regex ManyBlankRx();
}
