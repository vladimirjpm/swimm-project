using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Swimm.Application.Abstractions;
using Swimm.Infrastructure;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Выбор реализации IEmailSender по конфигу (фаза 4.4): Email:Smtp:Host задан → SmtpEmailSender
/// с привязанными опциями; не задан/пуст → дев-LoggingEmailSender.
/// </summary>
public class EmailSenderRegistrationTests
{
    private static IEmailSender Resolve(Dictionary<string, string?> settings, out ServiceProvider provider)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(configuration);
        provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IEmailSender>();
    }

    [Fact]
    public void NoSmtpHost_UsesLoggingEmailSender()
    {
        using var _ = ResolveAndAssert(new(), typeof(LoggingEmailSender));
    }

    [Fact]
    public void EmptySmtpHost_UsesLoggingEmailSender()
    {
        using var _ = ResolveAndAssert(new() { ["Email:Smtp:Host"] = "  " }, typeof(LoggingEmailSender));
    }

    [Fact]
    public void SmtpHostConfigured_UsesSmtpEmailSender_AndBindsOptions()
    {
        var settings = new Dictionary<string, string?>
        {
            ["Email:Smtp:Host"] = "smtp.example.com",
            ["Email:Smtp:Port"] = "2525",
            ["Email:Smtp:User"] = "mailer",
            ["Email:Smtp:From"] = "noreply@swimhub.example",
        };
        var sender = Resolve(settings, out var provider);
        using (provider)
        {
            Assert.IsType<SmtpEmailSender>(sender);

            var options = provider.GetRequiredService<IOptions<SmtpEmailOptions>>().Value;
            Assert.Equal("smtp.example.com", options.Host);
            Assert.Equal(2525, options.Port);
            Assert.Equal("mailer", options.User);
            Assert.Equal("noreply@swimhub.example", options.From);
            Assert.True(options.EnableSsl); // дефолт
        }
    }

    private ServiceProvider ResolveAndAssert(Dictionary<string, string?> settings, Type expected)
    {
        var sender = Resolve(settings, out var provider);
        Assert.IsType(expected, sender);
        return provider;
    }
}
