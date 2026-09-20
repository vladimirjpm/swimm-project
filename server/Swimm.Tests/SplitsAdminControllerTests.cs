using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Swimm.API.Controllers;
using Swimm.Application.Abstractions;
using Swimm.Application.Mapping;
using System.Text.Json;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Кнопка «⏱ Промежуточные» на /Admin/Competitions (Д5,
/// docs/plans/splits-attach-without-repull-plan.md), серверная сторона.
///
/// Сама доклейка проверена в <see cref="SplitAttachMatcherTests"/> и
/// <see cref="SplitAttachServiceTests"/>; здесь — ровно три решения контроллера:
/// сухой прогон не пишет в аудит, боевой с записями пишет, а сорвавшийся источник
/// возвращается ответом с <c>ok: false</c>, а не 500.
/// </summary>
public class SplitsAdminControllerTests
{
    /// <summary>Отдаёт заготовленный итог и запоминает, с какими аргументами его позвали.</summary>
    private sealed class AttachStub(SplitAttachResult result, Exception? throws = null) : ISplitAttachService
    {
        public bool? ApplyArg { get; private set; }
        public int? LogligArg { get; private set; }

        public Task<SplitAttachResult> AttachAsync(
            int competitionId, int? logligId, bool apply, CancellationToken ct = default)
        {
            ApplyArg = apply;
            LogligArg = logligId;
            return throws is not null ? Task.FromException<SplitAttachResult>(throws) : Task.FromResult(result);
        }
    }

    private sealed class AuditSpy : IAdminAuditService
    {
        public List<string> Actions { get; } = [];

        public Task LogAsync(string action, string entityType, string? entityId = null,
            string? summary = null, object? details = null, CancellationToken ct = default)
        {
            Actions.Add(action);
            return Task.CompletedTask;
        }
    }

    /// <param name="writes">Сколько ног и строк операция записала (или записала бы).</param>
    private static SplitAttachResult Outcome(bool applied, int writes) => new(
        applied, LogligId: 15132,
        Days: [new SplitAttachDay(1581, "чемпионат", "21/07/2026", writes, writes)],
        Report: new SplitAttachReport(1, 1, 0, 0, 0, 0, 0, 1, 1, 0, 0)
        {
            LegWrites = writes, SwimWrites = writes
        },
        Message: "источник loglig 15132");

    private static (SplitsAdminController Controller, AuditSpy Audit) Build(AttachStub attach)
    {
        var audit = new AuditSpy();
        return (new SplitsAdminController(attach, audit,
            NullLogger<SplitsAdminController>.Instance), audit);
    }

    private static JsonElement Body(IActionResult result) =>
        JsonSerializer.SerializeToElement(Assert.IsType<OkObjectResult>(result).Value);

    /// <summary>
    /// Дефолт ручки — СУХОЙ прогон: сервис зовётся с apply=false и в аудит ничего не уходит.
    /// Кнопка на первый клик всегда попадает именно сюда, поэтому дефолт важнее удобства.
    /// </summary>
    [Fact]
    public async Task DryRun_DoesNotApply_AndWritesNoAudit()
    {
        var attach = new AttachStub(Outcome(applied: false, writes: 7));
        var (controller, audit) = Build(attach);

        var body = Body(await controller.Attach(1581));

        Assert.False(attach.ApplyArg);
        Assert.True(body.GetProperty("dryRun").GetBoolean());
        Assert.False(body.GetProperty("applied").GetBoolean());
        Assert.Equal(7, body.GetProperty("legWrites").GetInt32());
        Assert.Empty(audit.Actions);
    }

    /// <summary>
    /// Боевой прогон, который что-то записал, обязан оставить след в аудите; прогон, которому
    /// писать было нечего (повторный — операция идемпотентна), — не обязан, иначе журнал
    /// заполняется пустыми строками. Заодно проверяем, что явный loglig доезжает до сервиса.
    /// </summary>
    [Theory]
    [InlineData(5, 1)]
    [InlineData(0, 0)]
    public async Task Apply_LogsAudit_OnlyWhenSomethingWasWritten(int writes, int auditRows)
    {
        var attach = new AttachStub(Outcome(applied: true, writes));
        var (controller, audit) = Build(attach);

        var body = Body(await controller.Attach(1581, apply: true, loglig: 15132));

        Assert.True(attach.ApplyArg);
        Assert.Equal(15132, attach.LogligArg);
        Assert.False(body.GetProperty("dryRun").GetBoolean());
        Assert.Equal(auditRows, audit.Actions.Count);
        Assert.All(audit.Actions, a => Assert.Equal("competition.attach-splits", a));
    }

    /// <summary>
    /// Источник недоступен или не разобрался — обычный исход, а не сбой сервера: панель должна
    /// показать причину, поэтому ответ 200 с <c>ok: false</c>, а не 500 с пустым телом.
    /// </summary>
    [Fact]
    public async Task SourceFailure_ReturnsOkFalse_WithReason()
    {
        var attach = new AttachStub(Outcome(applied: false, 0),
            throws: new InvalidOperationException("loglig не отдал PDF"));
        var (controller, audit) = Build(attach);

        var body = Body(await controller.Attach(1581, apply: true));

        Assert.False(body.GetProperty("ok").GetBoolean());
        Assert.Contains("loglig не отдал PDF", body.GetProperty("message").GetString());
        Assert.Empty(audit.Actions);
    }
}
