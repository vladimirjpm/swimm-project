using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Swimm.Domain.Entities;

namespace Swimm.Infrastructure.Data.Configurations;

internal sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> entity)
    {
        entity.ToTable("Categories");
        entity.HasIndex(e => e.Key).IsUnique();

        entity.HasData(
            // Возрастная лестница (2026-07-31): ילדים 8–11 Kids, צעירים 11–14 Young,
            // נוער Juniors, בוגרים Adults, мастерс Masters. Ключи приведены к подписям
            // (миграция CategoryLadderRenameAndHebrew) — старые ?category= уводятся
            // алиасами в results-categories.ts.
            new Category { Id = 3, Key = "results-kids-team",      Name = "Kids",    NameHe = "ילדים",  DisplayOrder = 1, Badge = "K", MinAge = 8,  MaxAge = 11 },
            new Category { Id = 4, Key = "results-youth-team",     Name = "Young",   NameHe = "צעירים", DisplayOrder = 2, Badge = "Y", MinAge = 11, MaxAge = 14 },
            new Category { Id = 1, Key = "results-junior-results", Name = "Juniors", NameHe = "נוער",   DisplayOrder = 3, Badge = "J", MinAge = 14, MaxAge = 17 },
            new Category { Id = 7, Key = "results-main",           Name = "Adults",  NameHe = "בוגרים", DisplayOrder = 4, Badge = "A", MinAge = 17 },
            new Category { Id = 2, Key = "results-masters",        Name = "Masters", NameHe = "מסטרס",  DisplayOrder = 5, Badge = "M" }
        );
    }
}

internal sealed class CategoryCompetitionConfiguration : IEntityTypeConfiguration<CategoryCompetition>
{
    public void Configure(EntityTypeBuilder<CategoryCompetition> entity)
    {
        entity.ToTable("CategoryCompetitions");
        entity.HasIndex(e => new { e.CategoryId, e.CompetitionId }).IsUnique();

        entity.HasOne(e => e.Category)
            .WithMany(c => c.Competitions)
            .HasForeignKey(e => e.CategoryId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne(e => e.Competition)
            .WithMany()
            .HasForeignKey(e => e.CompetitionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
