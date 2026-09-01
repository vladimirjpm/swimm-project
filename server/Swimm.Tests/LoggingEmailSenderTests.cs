using Microsoft.Extensions.Logging;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Сторож приватности: тело письма содержит одноразовые токены подтверждения почты и сброса
/// пароля. В деве оно пишется в консоль намеренно (иначе сценарий не пройти без SMTP), но за
/// пределами Development логи доступны шире — там тела быть не должно.
/// docs/plans/azure-deploy-plan.md Б14.
/// </summary>
public class LoggingEmailSenderTests
{
    private const string Body = "<a href=\"https://swimm.example/auth/reset-password?token=SEKRET-TOKEN-123\">Reset</a>";

    [Fact]
    public async Task NotDevelopment_DoesNotLeakBodyOrToken()
    {
        var sink = new CapturingLoggerProvider();
        var sender = new LoggingEmailSender(LoggerFor(sink), logBody: false);

        await sender.SendAsync("swimmer@example.com", "Reset your password", Body);

        var written = string.Join("\n", sink.Messages);
        Assert.DoesNotContain("SEKRET-TOKEN-123", written);
        Assert.DoesNotContain("reset-password?token=", written);
        // Адрес и тема остаются: без них по логу не понять, что именно не ушло.
        Assert.Contains("swimmer@example.com", written);
        Assert.Contains("Reset your password", written);
        // Неотправленное письмо — отказ конфигурации, а не рядовое событие.
        Assert.Contains(LogLevel.Error, sink.Levels);
    }

    [Fact]
    public async Task Development_LogsBodySoTheLinkCanBeCopiedFromConsole()
    {
        var sink = new CapturingLoggerProvider();
        var sender = new LoggingEmailSender(LoggerFor(sink), logBody: true);

        await sender.SendAsync("swimmer@example.com", "Reset your password", Body);

        Assert.Contains("SEKRET-TOKEN-123", string.Join("\n", sink.Messages));
    }

    private static ILogger<LoggingEmailSender> LoggerFor(CapturingLoggerProvider sink)
    {
        using var factory = LoggerFactory.Create(b => b.AddProvider(sink).SetMinimumLevel(LogLevel.Trace));
        return factory.CreateLogger<LoggingEmailSender>();
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider, ILogger
    {
        public List<string> Messages { get; } = [];
        public List<LogLevel> Levels { get; } = [];

        public ILogger CreateLogger(string categoryName) => this;
        public void Dispose() { }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Levels.Add(logLevel);
            Messages.Add(formatter(state, exception));
        }
    }
}
