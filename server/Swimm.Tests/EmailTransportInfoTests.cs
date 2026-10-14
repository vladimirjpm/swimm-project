using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Swimm.API.Pages.Admin;
using Swimm.API.Pages.Admin.Shared;
using Swimm.Application.Abstractions;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Блок «Почта» в админке (/Admin/Settings#email + строка дашборда): каждая реализация
/// IEmailSender честно сообщает, куда уходят письма, а кнопка тестового письма отличает
/// «ушло», «ошибка SMTP» и «SMTP не настроен». Ради ProdAdmin: письма о заявках клубов шлёт
/// локальная админка, и без Email:Smtp они молча уходили бы в лог.
/// </summary>
public class EmailTransportInfoTests
{
    private const string AdminEmail = "admin@swimhub.test";

    private static SmtpEmailSender Smtp(string from = "noreply@swimhub.co.il") => new(
        Options.Create(new SmtpEmailOptions
        {
            Host = "smtp.resend.com", Port = 587, User = "resend",
            Password = "re_secret", From = from, FromName = "SwimHub",
        }),
        NullLogger<SmtpEmailSender>.Instance);

    // --- Что сообщают реализации ---

    [Fact]
    public void Smtp_ReportsHostPortAndSender_WithoutSecret()
    {
        var t = Smtp().Transport;

        Assert.Equal(new EmailTransportInfo(EmailTransportKind.Smtp, "smtp.resend.com", 587,
            "noreply@swimhub.co.il", "SwimHub"), t);
        Assert.True(t.IsHealthy);
        Assert.DoesNotContain("re_secret", t.ToString());
    }

    [Fact]
    public void Smtp_EmptyFrom_FallsBackToUser_LikeSendAsync()
    {
        Assert.Equal("resend", Smtp(from: "").Transport.From);
    }

    [Theory]
    [InlineData(true, EmailTransportKind.DevLog, true)]
    [InlineData(false, EmailTransportKind.Disabled, false)]
    public void Logging_DevIsHealthy_OutsideDevIsDisabled(bool logBody, EmailTransportKind kind, bool healthy)
    {
        var t = new LoggingEmailSender(NullLogger<LoggingEmailSender>.Instance, logBody).Transport;

        Assert.Equal(kind, t.Kind);
        Assert.Equal(healthy, t.IsHealthy);
    }

    [Fact]
    public void Stub_WithoutOverride_IsUnknown_AndNotHealthy()
    {
        IEmailSender stub = new BareSender();

        Assert.Same(EmailTransportInfo.Unknown, stub.Transport);
        Assert.False(stub.Transport.IsHealthy);
    }

    [Fact]
    public void View_DisabledWarnsAboutProdAdmin_AndBlocksTest()
    {
        var t = new EmailTransportInfo(EmailTransportKind.Disabled);

        Assert.StartsWith("⚠", EmailStatusView.ShortLabel(t));
        Assert.Contains("ProdAdmin", EmailStatusView.Detail(t));
        Assert.False(EmailStatusView.CanSendTest(t));
    }

    // --- Кнопка «Отправить тестовое письмо мне» ---

    [Fact]
    public async Task TestEmail_Smtp_SendsToCurrentAdmin_AndAudits()
    {
        var sender = new StubSender(new EmailTransportInfo(EmailTransportKind.Smtp, "smtp.resend.com", 587));
        var (page, audit) = Page(sender);

        await page.OnPostTestEmailAsync(CancellationToken.None);

        Assert.Equal(AdminEmail, Assert.Single(sender.SentTo));
        Assert.False(page.EmailTestFailed);
        Assert.Contains(AdminEmail, page.EmailTestResult);
        audit.Verify(a => a.LogAsync("email.test", "Email", null,
            It.Is<string>(s => s.Contains("OK")), null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TestEmail_SmtpError_ShowsRootCause_AndAuditsFailure()
    {
        var sender = new StubSender(new EmailTransportInfo(EmailTransportKind.Smtp, "smtp.resend.com", 587),
            fail: new InvalidOperationException("Failure sending mail.", new Exception("535 Authentication failed")));
        var (page, audit) = Page(sender);

        await page.OnPostTestEmailAsync(CancellationToken.None);

        Assert.True(page.EmailTestFailed);
        Assert.Contains("535 Authentication failed", page.EmailTestResult);
        audit.Verify(a => a.LogAsync("email.test", "Email", null,
            It.Is<string>(s => s.Contains("ошибка")), null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TestEmail_SmtpNotConfigured_DoesNotPretendToSend()
    {
        var sender = new StubSender(new EmailTransportInfo(EmailTransportKind.Disabled));
        var (page, audit) = Page(sender);

        await page.OnPostTestEmailAsync(CancellationToken.None);

        Assert.Empty(sender.SentTo);
        Assert.True(page.EmailTestFailed);
        audit.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task TestEmail_ActorWithoutEmail_SendsNothing()
    {
        var sender = new StubSender(new EmailTransportInfo(EmailTransportKind.Smtp, "smtp.resend.com", 587));
        var (page, _) = Page(sender, actorName: "cli");

        await page.OnPostTestEmailAsync(CancellationToken.None);

        Assert.Empty(sender.SentTo);
        Assert.True(page.EmailTestFailed);
    }

    private static (SettingsModel page, Mock<IAdminAuditService> audit) Page(
        IEmailSender sender, string actorName = AdminEmail)
    {
        var actor = new Mock<ICurrentActor>();
        actor.SetupGet(a => a.Name).Returns(actorName);
        var audit = new Mock<IAdminAuditService>();
        return (new SettingsModel(sender, actor.Object, audit.Object), audit);
    }

    /// <summary>Старая заглушка, не знающая про Transport, — берёт дефолт интерфейса.</summary>
    private sealed class BareSender : IEmailSender
    {
        public Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private sealed class StubSender(EmailTransportInfo? transport = null, Exception? fail = null) : IEmailSender
    {
        public List<string> SentTo { get; } = [];

        public EmailTransportInfo Transport => transport ?? EmailTransportInfo.Unknown;

        public Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default)
        {
            if (fail != null) throw fail;
            SentTo.Add(toEmail);
            return Task.CompletedTask;
        }
    }
}
