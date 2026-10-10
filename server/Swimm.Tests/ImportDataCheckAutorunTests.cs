using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Автозапуск реестра проверок в конце импорта (фаза Д5, решение Р13). Смысл фазы —
/// человеку не надо догадываться открыть /Admin/Health, поэтому проверяем ровно две вещи:
/// прогон случился с правильным триггером, и упавший прогон не роняет уже сохранённый импорт.
/// </summary>
public class ImportDataCheckAutorunTests
{
    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private sealed class NullCache : ICacheService
    {
        public Task<T?> GetAsync<T>(string key) => Task.FromResult<T?>(default);
        public Task SetAsync<T>(string key, T value, TimeSpan ttl) => Task.CompletedTask;
        public Task RemoveAsync(string key) => Task.CompletedTask;
        public Task InvalidateAllAsync() => Task.CompletedTask;
    }

    /// <summary>
    /// Реестр-заглушка: запоминает триггер и отдаёт заданные ОТКРЫТЫЕ счётчики; <paramref name="accepted"/> —
    /// принятые предупреждения, которые прогон тоже находит, но работой они уже не считаются.
    /// </summary>
    private sealed class FakeRunner(int errors = 0, int warnings = 0, int accepted = 0, bool throws = false) : IDataCheckRunner
    {
        public string? Trigger { get; private set; }
        public int Runs { get; private set; }

        public Task<DataCheckRunDto> RunAllAsync(string trigger, CancellationToken ct = default)
        {
            Trigger = trigger;
            Runs++;
            if (throws) throw new InvalidOperationException("реестр упал");
            return Task.FromResult(new DataCheckRunDto(
                1, DateTime.UtcNow, DateTime.UtcNow, trigger, errors, warnings + accepted, 0, 0));
        }

        public Task<IReadOnlyList<DataCheckGroupDto>> GetCurrentAsync(CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<DataCheckRunDto>> GetHistoryAsync(int limit = 20, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<(DataCheckRunDto? LastRun, IReadOnlyList<DataCheckStateDto> States)> GetStateAsync(
            CancellationToken ct = default) =>
            Task.FromResult<(DataCheckRunDto?, IReadOnlyList<DataCheckStateDto>)>((null,
            [
                new("e", DataCheckSeverity.Error, errors, errors, false, 1, DateTime.UtcNow),
                new("w", DataCheckSeverity.Warning, warnings + accepted, warnings + accepted, false, 1, DateTime.UtcNow, accepted),
            ]));
        public Task<bool> AcceptAsync(int findingId, string? note, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<int?> FixSwimmerGenderAsync(int findingId, string gender, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<int?> SetSwimmerGenderAsync(int swimmerId, string gender, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<int?> AlignSwimmerGenderAsync(int findingId, string gender, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<bool> FixCompetitionClubRuleAsync(int findingId, int ruleId, CancellationToken ct = default) =>
            Task.FromResult(false);
        public Task<(int Findings, int Rows)> FixAllKnownSwimmerGendersAsync(CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<bool> ReopenAsync(int findingId, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private static object Item(string lastName, int lane) => new
    {
        country = "ISR",
        competition = "Meet",
        date = "01/06/2026",
        event_style_name = "Freestyle",
        event_style_len = "50",
        event_style_gender = "male",
        pool_type = "25m",
        position = lane,
        heat = 1,
        lane,
        last_name = lastName,
        first_name = "Tal",
        birth_year = 2012,
        club = "Club",
        time = "00:30.00"
    };

    private static Stream ToStream(object payload) =>
        new MemoryStream(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload)));

    [Fact]
    public async Task Import_RunsAllChecks_AndReportsFindings()
    {
        await using var db = CreateDb(nameof(Import_RunsAllChecks_AndReportsFindings));
        var runner = new FakeRunner(errors: 2, warnings: 1);

        var result = await new JsonImportService(db, new NullCache(), null, runner)
            .ImportAsync(ToStream(new[] { Item("A", 1), Item("B", 2) }), "meet.json");

        Assert.Equal(1, runner.Runs);
        Assert.Equal("import", runner.Trigger);
        Assert.Equal(2, result.DataCheckErrors);
        Assert.Equal(1, result.DataCheckWarnings);
        Assert.Contains("ошибок — 2", result.DataChecks);
        Assert.Contains(result.DiagnosticLog, l => l.Contains("Проверки данных"));
    }

    [Fact]
    public async Task Import_CleanChecks_SaySoExplicitly()
    {
        // «Проверки прогнаны и молчат» — это не то же самое, что «проверок не было»;
        // молчание в логе прочиталось бы как второе.
        await using var db = CreateDb(nameof(Import_CleanChecks_SaySoExplicitly));

        var result = await new JsonImportService(db, new NullCache(), null, new FakeRunner())
            .ImportAsync(ToStream(new[] { Item("A", 1) }), "meet.json");

        Assert.Equal("Проверки данных: чисто.", result.DataChecks);
    }

    [Fact]
    public async Task Import_AcceptedFindings_AreNotReportedAsWarnings()
    {
        // Перезалив Эйлата-2025 10.10.2026 напечатал «предупреждений — 31»: все 31 были приняты
        // раньше, а строка читалась как «импорт что-то нашёл». Принятые — отдельным числом.
        await using var db = CreateDb(nameof(Import_AcceptedFindings_AreNotReportedAsWarnings));

        var result = await new JsonImportService(db, new NullCache(), null, new FakeRunner(accepted: 31))
            .ImportAsync(ToStream(new[] { Item("A", 1) }), "meet.json");

        Assert.Equal(0, result.DataCheckErrors);
        Assert.Equal(0, result.DataCheckWarnings);
        Assert.Equal("Проверки данных: открытых нет (принятых раньше — 31).", result.DataChecks);
    }

    [Fact]
    public async Task Import_OpenAndAcceptedFindings_BothNamed()
    {
        await using var db = CreateDb(nameof(Import_OpenAndAcceptedFindings_BothNamed));

        var result = await new JsonImportService(db, new NullCache(), null, new FakeRunner(warnings: 1, accepted: 4))
            .ImportAsync(ToStream(new[] { Item("A", 1) }), "meet.json");

        Assert.Equal(1, result.DataCheckWarnings);
        Assert.Equal("Проверки данных: открытых ошибок — 0, предупреждений — 1; принятых раньше — 4.", result.DataChecks);
    }

    [Fact]
    public async Task ChecksFailure_DoesNotBreakImport()
    {
        // Прибор не имеет права уронить импорт: результаты уже закоммичены.
        await using var db = CreateDb(nameof(ChecksFailure_DoesNotBreakImport));

        var result = await new JsonImportService(db, new NullCache(), null, new FakeRunner(throws: true))
            .ImportAsync(ToStream(new[] { Item("A", 1) }), "meet.json");

        Assert.Empty(result.ErrorMessages);
        Assert.Equal(1, result.Created);
        Assert.Equal("", result.DataChecks);
        Assert.Contains(result.DiagnosticLog, l => l.Contains("Проверки данных не выполнены"));
    }

    /// <summary>Сторож невозможных этапов-заглушка: запоминает область и отдаёт одну команду.</summary>
    private sealed class FakeLegCleanup(bool throws = false) : IRelayLegCleanupService
    {
        public IReadOnlyCollection<int>? Scope { get; private set; }
        public bool? Applied { get; private set; }

        public Task<RelayLegCleanupReport> RunAsync(
            bool apply, IReadOnlyCollection<int>? competitionIds = null, CancellationToken ct = default)
        {
            if (throws) throw new InvalidOperationException("сторож упал");
            (Applied, Scope) = (apply, competitionIds);
            return Task.FromResult(new RelayLegCleanupReport(apply, 1,
                [new RelayLegCleanupRow(7, 1, "4X50", "freestyle", "02:42.64", "нога 2: …", ["00:39.92", "00:00.69"])], 2));
        }
    }

    [Fact]
    public async Task Import_ClearsImplausibleLegs_OfItsOwnCompetitions()
    {
        // Р82: сборка мастерских эстафет кладёт этапы ног в обход доклейки — перезалив #1482
        // 10.10.2026 вернул обнулённый этап 00:00.69. Импорт обязан прогнать сторож по СВОИМ дням.
        await using var db = CreateDb(nameof(Import_ClearsImplausibleLegs_OfItsOwnCompetitions));
        var legs = new FakeLegCleanup();

        var result = await new JsonImportService(db, new NullCache(), legCleanup: legs)
            .ImportAsync(ToStream(new[] { Item("A", 1) }), "meet.json");

        var compId = (await db.Competitions.SingleAsync()).Id;
        Assert.True(legs.Applied);
        Assert.Equal([compId], legs.Scope);
        Assert.Contains(result.DiagnosticLog, l => l.Contains("невозможный этап у 1") && l.Contains("обнулено 2"));
    }

    [Fact]
    public async Task LegCleanupFailure_DoesNotBreakImport()
    {
        await using var db = CreateDb(nameof(LegCleanupFailure_DoesNotBreakImport));

        var result = await new JsonImportService(db, new NullCache(), legCleanup: new FakeLegCleanup(throws: true))
            .ImportAsync(ToStream(new[] { Item("A", 1) }), "meet.json");

        Assert.Empty(result.ErrorMessages);
        Assert.Equal(1, result.Created);
        Assert.Contains(result.DiagnosticLog, l => l.Contains("Этапы эстафет: проверка не удалась"));
    }
}
