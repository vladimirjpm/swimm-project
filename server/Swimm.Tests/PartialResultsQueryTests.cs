using Microsoft.EntityFrameworkCore;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Repositories;
using Xunit;
using Xunit.Abstractions;

namespace Swimm.Tests;

/// <summary>
/// Условие «отметка видна» (Р114) уходит в SQL ровно в форме фильтра частичного индекса
/// <c>IX_Competitions_PartialResults_Shown</c> — иначе планировщик индекс не возьмёт, а
/// соревнований будут сотни тысяч. Подключение не нужно: SQL строит провайдер Npgsql.
/// </summary>
public class PartialResultsQueryTests(ITestOutputHelper output)
{
    private static SwimmDbContext NpgsqlModelOnly() =>
        new(new DbContextOptionsBuilder<SwimmDbContext>()
            .UseNpgsql("Host=localhost;Database=none").Options);

    [Fact]
    public void Shown_predicate_is_sql_and_matches_the_partial_index_filter()
    {
        using var db = NpgsqlModelOnly();
        var sql = db.Competitions.WherePartialShown().Select(c => c.Id).ToQueryString();
        output.WriteLine(sql);

        Assert.Contains("c.\"PartialResults\" IS NOT NULL AND c.\"PartialResultsAcceptedAt\" IS NULL", sql);

        var index = db.Model.FindEntityType(typeof(Swimm.Domain.Entities.Competition))!
            .GetIndexes().Single(i => i.GetDatabaseName() == "IX_Competitions_PartialResults_Shown");
        Assert.Equal("\"PartialResults\" IS NOT NULL AND \"PartialResultsAcceptedAt\" IS NULL", index.GetFilter());
        Assert.Equal(["Id"], index.Properties.Select(p => p.Name));
    }
}
