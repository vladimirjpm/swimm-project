using Microsoft.EntityFrameworkCore;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Repositories;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// «С ошибками» /Admin/Competitions на НАСТОЯЩЕМ Postgres: запрос находок (UNION ALL четырёх веток +
/// GROUP BY), журнал доклеек «последняя на день» и срезанные проверки транслируются в SQL; фильтр
/// сочетается с поиском (ILIKE — его у InMemory нет) и с <c>?state=</c>. Только читает локальную
/// базу; нет базы или открытых находок по старту — пропуск.
/// </summary>
[Collection(LiveDbCollection.Name)]
public class CompetitionProblemsPgTests
{
    private const string Conn =
        "Host=localhost;Port=5445;Database=swimm;Username=swimm;Password=swimm_local_dev";

    private static SwimmDbContext? TryCreate()
    {
        var db = new SwimmDbContext(new DbContextOptionsBuilder<SwimmDbContext>().UseNpgsql(Conn).Options);
        try { if (!db.Database.CanConnect()) { db.Dispose(); return null; } return db; }
        catch { db.Dispose(); return null; }
    }

    private static Task<UnifiedCompetitionList> Problems(SwimmDbContext db, string? q = null, string? state = null, int size = 500) =>
        new CompetitionAdminRepository(db).GetUnifiedAsync(q, null, null, null, showSynthetic: true, month: null, 1, size,
            qualityFilter: CompetitionProblemRules.Filter, discipline: "all", state: state);

    [Fact]
    public async Task ProblemsFilter_TranslatesOnPostgres_EveryRowHasAProblem()
    {
        await using var db = TryCreate();
        if (db == null) return;

        var list = await Problems(db);
        Assert.All(list.Page.Items, u => Assert.True(CompetitionProblemRules.HasProblem(u)));
        Assert.NotNull(list.SplitNotes);
        // Тексты находок — у строк страницы с находками (второй запрос транслировался).
        Assert.All(list.Page.Items.Where(u => u.Health.Count > 0), u => Assert.NotEmpty(u.HealthFindings));
    }

    [Fact]
    public async Task CompetitionFinding_CountedOnItsRow_AndFilterCombinesWithSearchAndState()
    {
        await using var db = TryCreate();
        if (db == null) return;

        var f = await db.DataCheckFindings.AsNoTracking()
            .Where(x => x.Resolution == null && x.Severity >= 1 && x.EntityType == "Competition" && x.EntityId != null
                        && db.Competitions.Any(c => c.Id == x.EntityId && c.EventId == null))
            .OrderBy(x => x.Id).FirstOrDefaultAsync();
        if (f == null) return;
        var expected = await db.DataCheckFindings.CountAsync(x => x.Resolution == null && x.EntityType == "Competition"
            && x.EntityId == f.EntityId && x.CheckId == f.CheckId && x.Severity == f.Severity);

        var bySearch = await Problems(db, q: f.EntityId!.Value.ToString());
        var row = Assert.Single(bySearch.Page.Items, u => u.Db?.Single?.Id == f.EntityId);
        Assert.Contains(new CompetitionHealthCount(f.CheckId, Math.Min(f.Severity, 2), expected), row.Health);

        // ?state= режет уже отфильтрованное: сумма по корзинам = весь фильтр.
        var all = (await Problems(db)).Page.TotalCount;
        var byBuckets = 0;
        foreach (var st in new[] { CompetitionStateFilters.Waiting, CompetitionStateFilters.Ahead, CompetitionStateFilters.Closed })
            byBuckets += (await Problems(db, state: st)).Page.TotalCount;
        Assert.Equal(all, byBuckets);
    }
}
