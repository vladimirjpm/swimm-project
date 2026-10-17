using System.Net.Mail;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Swimm.Infrastructure.Services;
using Swimm.Infrastructure.Services.Email;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Общий вид писем сайта (EmailLayout + SmtpEmailSender.BuildMessage): шапка с логотипом
/// вложением, текстовая версия рядом с HTML, ссылки-действия видны в обеих.
/// </summary>
public class EmailLayoutTests
{
    private const string Site = "https://swimhub.co.il";
    private const string Link = "https://swimhub.co.il/auth/verify-email?token=abc&x=1";

    [Fact]
    public void Logo_IsEmbeddedPng()
    {
        var png = EmailLayout.LogoPng();

        Assert.True(png.Length > 1000);
        Assert.Equal(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G' }, png[..4]);
    }

    [Fact]
    public void Html_WrapsBody_WithCidLogoAndSiteFooter()
    {
        var html = EmailLayout.Html("SwimHub — test", "<p>Hello <a href=\"https://x.test\">x</a></p>", Site);

        Assert.StartsWith("<!DOCTYPE html>", html);
        Assert.Contains($"src=\"cid:{EmailLayout.LogoContentId}\"", html);
        Assert.Contains("<p>Hello <a style=\"color:", html);
        Assert.Contains(">swimhub.co.il</a>", html);
        Assert.Contains("<title>SwimHub — test</title>", html);
    }

    [Fact]
    public void Html_KeepsCallerLinkStyle()
    {
        var html = EmailLayout.Html("s", EmailLayout.Button(Link, "Confirm email"), Site);

        // Кнопка несёт свой стиль — цвет ссылок к ней не дописывается второй раз.
        Assert.DoesNotContain("<a style=\"color:#0b6e8a\" href", html);
        Assert.Contains("color:#ffffff", html);
    }

    [Fact]
    public void Button_EncodesUrlAndLabel()
    {
        var b = EmailLayout.Button(Link, "Confirm <email>");

        Assert.Contains("href=\"https://swimhub.co.il/auth/verify-email?token=abc&amp;x=1\"", b);
        Assert.Contains("Confirm &lt;email&gt;", b);
    }

    [Fact]
    public void PlainText_PrintsLinksAndLineBreaks()
    {
        var body = "<p>Welcome to SwimHub!</p>" + EmailLayout.Button(Link, "Confirm email") +
                   $"<p>Or open:<br><a href=\"{Link}\">{Link}</a></p>";

        var text = EmailLayout.PlainText(body, Site);

        Assert.Equal(
            "Welcome to SwimHub!\n" +
            $"Confirm email: {Link}\n\n" +
            "Or open:\n" +
            $"{Link}\n\n" +
            "--\nSwimHub — swimming results in Israel\nhttps://swimhub.co.il\n",
            text);
    }

    [Fact]
    public void PlainText_DecodesEntities()
    {
        var text = EmailLayout.PlainText("<p>Group <b>A &amp; B</b> &quot;ok&quot;</p>", Site);

        Assert.StartsWith("Group A & B \"ok\"\n", text);
    }

    [Fact]
    public void BuildMessage_IsPlainPlusHtmlWithLinkedLogo()
    {
        var sender = new SmtpEmailSender(
            Options.Create(new SmtpEmailOptions { Host = "smtp.test", From = "noreply@swimhub.co.il" }),
            NullLogger<SmtpEmailSender>.Instance);

        using var m = sender.BuildMessage("user@example.com", "SwimHub — test", "<p>Hi</p>");

        Assert.Equal(2, m.AlternateViews.Count);
        Assert.Equal("text/plain", m.AlternateViews[0].ContentType.MediaType);
        var html = m.AlternateViews[1];
        Assert.Equal("text/html", html.ContentType.MediaType);
        var logo = Assert.Single(html.LinkedResources);
        Assert.Equal(EmailLayout.LogoContentId, logo.ContentId);
        Assert.Equal("image/png", logo.ContentType.MediaType);
    }

    [Fact]
    public void BuildMessage_WritesValidMime()
    {
        var sender = new SmtpEmailSender(
            Options.Create(new SmtpEmailOptions { Host = "smtp.test", From = "noreply@swimhub.co.il" }),
            NullLogger<SmtpEmailSender>.Instance);
        var dir = Directory.CreateTempSubdirectory("swimm-eml-");
        try
        {
            using var m = sender.BuildMessage("user@example.com", "SwimHub — test", "<p>Hi</p>");
            using var client = new SmtpClient
            {
                DeliveryMethod = SmtpDeliveryMethod.SpecifiedPickupDirectory,
                PickupDirectoryLocation = dir.FullName,
            };
            client.Send(m);

            var eml = File.ReadAllText(Assert.Single(dir.GetFiles("*.eml")).FullName);
            Assert.Contains("multipart/alternative", eml);
            Assert.Contains("multipart/related", eml);
            Assert.Contains($"Content-ID: <{EmailLayout.LogoContentId}>", eml);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }
}
