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
/// <see cref="SplitAttachServiceTests"/>, запись итога в аудит — в
/// <see cref="SplitAutoAttachServiceTests"/>; здесь — решения контроллера: сухой прогон идёт
/// прямо в доклейку и в аудит не попадает, боевой — через <see cref="ISplitAutoAttachService"/>
/// (итог всем дням), а сорвавшийся источник возвращается ответом с <c>ok: false</c>, а не 500.
/// </summary>
public class SplitsAdminControllerTests
{
    /// <summary>Отдаёт заготовленный итог и запоминает, с какими аргументами его позвали.</summary>
    private sealed class AttachStub(SplitAttachResult result, Exception? throws = null) : ISplitAttachService
    {
        public bool? ApplyArg { get; private set; }

        public Task<SplitAttachResult> AttachAsync(
            int competitionId, int? logligId, bool apply, CancellationToken ct = default,
            IReadOnlyCollection<int>? onlyDays = null)
        {
            ApplyArg = apply;
            return throws is not null ? Task.FromException<SplitAttachResult>(throws) : Task.FromResult(result);
        }
    }

    private sealed class RecorderStub(SplitButtonRun run) : ISplitAutoAttachService
    {
        public (int Id, int? Loglig)? Called { get; private set; }

        public Task<SplitAutoOutcome> AttachAfterImportAsync(int orgCompId, Guid jobId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<SplitButtonRun> AttachByButtonAsync(int competitionId, int? logligId, CancellationToken ct = default)
        {
            Called = (competitionId, logligId);
            return Task.FromResult(run);
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

    private static SplitsAdminController Build(AttachStub attach, RecorderStub recorder) =>
        new(attach, recorder, NullLogger<SplitsAdminController>.Instance);

    private static JsonElement Body(IActionResult result) =>
        JsonSerializer.SerializeToElement(Assert.IsType<OkObjectResult>(result).Value);

    /// <summary>
    /// Дефолт ручки — СУХОЙ прогон: доклейка зовётся с apply=false, запись итога не зовётся вовсе.
    /// Кнопка на первый клик всегда попадает именно сюда, поэтому дефолт важнее удобства.
    /// </summary>
    [Fact]
    public async Task DryRun_DoesNotApply_AndDoesNotRecord()
    {
        var attach = new AttachStub(Outcome(applied: false, writes: 7));
        var recorder = new RecorderStub(new SplitButtonRun(Outcome(true, 0), SplitAutoKind.NothingNew, false));

        var body = Body(await Build(attach, recorder).Attach(1581));

        Assert.False(attach.ApplyArg);
        Assert.Null(recorder.Called);
        Assert.True(body.GetProperty("dryRun").GetBoolean());
        Assert.False(body.GetProperty("applied").GetBoolean());
        Assert.Equal(7, body.GetProperty("legWrites").GetInt32());
    }

    /// <summary>Боевой прогон идёт через запись итога (всем дням, всегда); явный loglig доезжает.</summary>
    [Fact]
    public async Task Apply_GoesThroughRecorder()
    {
        var attach = new AttachStub(Outcome(applied: true, writes: 5));
        var recorder = new RecorderStub(new SplitButtonRun(Outcome(true, 5), SplitAutoKind.Attached, false));

        var body = Body(await Build(attach, recorder).Attach(1581, apply: true, loglig: 15132));

        Assert.Equal((1581, (int?)15132), recorder.Called);
        Assert.Null(attach.ApplyArg);
        Assert.True(body.GetProperty("ok").GetBoolean());
        Assert.True(body.GetProperty("applied").GetBoolean());
        Assert.Equal(5, body.GetProperty("legWrites").GetInt32());
    }

    /// <summary>
    /// Источник недоступен — обычный исход, а не сбой сервера: панель должна показать причину,
    /// поэтому ответ 200 с <c>ok: false</c>.
    /// </summary>
    [Fact]
    public async Task Apply_SourceFailure_ReturnsOkFalse_WithReason()
    {
        var recorder = new RecorderStub(new SplitButtonRun(
            SplitAttachResult.Failed("не удалось: loglig не отдал PDF"), SplitAutoKind.Failed, Threw: true));

        var body = Body(await Build(new AttachStub(Outcome(false, 0)), recorder).Attach(1581, apply: true));

        Assert.False(body.GetProperty("ok").GetBoolean());
        Assert.Contains("loglig не отдал PDF", body.GetProperty("message").GetString());
    }

    [Fact]
    public async Task DryRun_SourceFailure_ReturnsOkFalse()
    {
        var attach = new AttachStub(Outcome(false, 0), throws: new InvalidOperationException("loglig не отдал PDF"));
        var recorder = new RecorderStub(new SplitButtonRun(Outcome(true, 0), SplitAutoKind.NothingNew, false));

        var body = Body(await Build(attach, recorder).Attach(1581));

        Assert.False(body.GetProperty("ok").GetBoolean());
        Assert.Null(recorder.Called);
    }
}
