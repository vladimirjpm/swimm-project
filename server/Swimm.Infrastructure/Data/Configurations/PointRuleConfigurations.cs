using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Swimm.Domain.Entities;

namespace Swimm.Infrastructure.Data.Configurations;

internal sealed class PointRuleClubsConfiguration : IEntityTypeConfiguration<PointRuleClubs>
{
    public void Configure(EntityTypeBuilder<PointRuleClubs> entity)
    {
        entity.ToTable("PointRulesClubs");
        entity.HasIndex(e => e.Version).IsUnique();

        entity.HasMany(e => e.Entries)
            .WithOne(e => e.Rule)
            .HasForeignKey(e => e.RuleId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasData(
            new PointRuleClubs
            {
                Id = 1,
                Version = "30pt.24pl.2025.01",
                EffectiveFrom = new DateOnly(2025, 1, 1),
                Description = "Israeli swimming club points system starting Jan 2025",
                Scope = "all",
                DefaultPoints = 0,
                MaxScoringPlace = 24
            },
            new PointRuleClubs
            {
                Id = 2,
                Version = "12pt.12pl.2025.01",
                EffectiveFrom = new DateOnly(2025, 1, 1),
                Description = "Masters club points system (places 1-12)",
                Scope = "masters",
                DefaultPoints = 0,
                MaxScoringPlace = 12
            }
        );
    }
}

internal sealed class PointRuleClubsEntryConfiguration : IEntityTypeConfiguration<PointRuleClubsEntry>
{
    public void Configure(EntityTypeBuilder<PointRuleClubsEntry> entity)
    {
        entity.ToTable("PointRulesClubsEntries");
        entity.HasIndex(e => new { e.RuleId, e.Place }).IsUnique();

        // Seed: правило 1 — шкала 24 мест (israelская система)
        entity.HasData(
            new PointRuleClubsEntry { Id =  1, RuleId = 1, Place =  1, Points = 30 },
            new PointRuleClubsEntry { Id =  2, RuleId = 1, Place =  2, Points = 28 },
            new PointRuleClubsEntry { Id =  3, RuleId = 1, Place =  3, Points = 26 },
            new PointRuleClubsEntry { Id =  4, RuleId = 1, Place =  4, Points = 24 },
            new PointRuleClubsEntry { Id =  5, RuleId = 1, Place =  5, Points = 23 },
            new PointRuleClubsEntry { Id =  6, RuleId = 1, Place =  6, Points = 22 },
            new PointRuleClubsEntry { Id =  7, RuleId = 1, Place =  7, Points = 21 },
            new PointRuleClubsEntry { Id =  8, RuleId = 1, Place =  8, Points = 20 },
            new PointRuleClubsEntry { Id =  9, RuleId = 1, Place =  9, Points = 19 },
            new PointRuleClubsEntry { Id = 10, RuleId = 1, Place = 10, Points = 18 },
            new PointRuleClubsEntry { Id = 11, RuleId = 1, Place = 11, Points = 16 },
            new PointRuleClubsEntry { Id = 12, RuleId = 1, Place = 12, Points = 15 },
            new PointRuleClubsEntry { Id = 13, RuleId = 1, Place = 13, Points = 14 },
            new PointRuleClubsEntry { Id = 14, RuleId = 1, Place = 14, Points = 13 },
            new PointRuleClubsEntry { Id = 15, RuleId = 1, Place = 15, Points = 12 },
            new PointRuleClubsEntry { Id = 16, RuleId = 1, Place = 16, Points = 11 },
            new PointRuleClubsEntry { Id = 17, RuleId = 1, Place = 17, Points = 10 },
            new PointRuleClubsEntry { Id = 18, RuleId = 1, Place = 18, Points =  9 },
            new PointRuleClubsEntry { Id = 19, RuleId = 1, Place = 19, Points =  8 },
            new PointRuleClubsEntry { Id = 20, RuleId = 1, Place = 20, Points =  7 },
            new PointRuleClubsEntry { Id = 21, RuleId = 1, Place = 21, Points =  5 },
            new PointRuleClubsEntry { Id = 22, RuleId = 1, Place = 22, Points =  3 },
            new PointRuleClubsEntry { Id = 23, RuleId = 1, Place = 23, Points =  2 },
            new PointRuleClubsEntry { Id = 24, RuleId = 1, Place = 24, Points =  1 },
            // Seed: правило 2 — Masters, шкала 12 мест
            new PointRuleClubsEntry { Id = 25, RuleId = 2, Place =  1, Points = 12 },
            new PointRuleClubsEntry { Id = 26, RuleId = 2, Place =  2, Points = 11 },
            new PointRuleClubsEntry { Id = 27, RuleId = 2, Place =  3, Points = 10 },
            new PointRuleClubsEntry { Id = 28, RuleId = 2, Place =  4, Points =  9 },
            new PointRuleClubsEntry { Id = 29, RuleId = 2, Place =  5, Points =  8 },
            new PointRuleClubsEntry { Id = 30, RuleId = 2, Place =  6, Points =  7 },
            new PointRuleClubsEntry { Id = 31, RuleId = 2, Place =  7, Points =  6 },
            new PointRuleClubsEntry { Id = 32, RuleId = 2, Place =  8, Points =  5 },
            new PointRuleClubsEntry { Id = 33, RuleId = 2, Place =  9, Points =  4 },
            new PointRuleClubsEntry { Id = 34, RuleId = 2, Place = 10, Points =  3 },
            new PointRuleClubsEntry { Id = 35, RuleId = 2, Place = 11, Points =  2 },
            new PointRuleClubsEntry { Id = 36, RuleId = 2, Place = 12, Points =  1 }
        );
    }
}

internal sealed class PointRuleSwimmersConfiguration : IEntityTypeConfiguration<PointRuleSwimmers>
{
    public void Configure(EntityTypeBuilder<PointRuleSwimmers> entity)
    {
        entity.ToTable("PointRulesSwimmers");
        entity.HasIndex(e => e.Version).IsUnique();

        entity.HasMany(e => e.Entries)
            .WithOne(e => e.Rule)
            .HasForeignKey(e => e.RuleId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class PointRuleSwimmersEntryConfiguration : IEntityTypeConfiguration<PointRuleSwimmersEntry>
{
    public void Configure(EntityTypeBuilder<PointRuleSwimmersEntry> entity)
    {
        entity.ToTable("PointRulesSwimmersEntries");
        entity.HasIndex(e => new { e.RuleId, e.Place }).IsUnique();
    }
}
