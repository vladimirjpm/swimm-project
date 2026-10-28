using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Swimm.Domain.Entities;

namespace Swimm.Infrastructure.Data.Configurations;

// Серии турниров и официальное награждение (docs/plans/official-awards-plan.md §5). Все шесть
// таблиц публичные: их читает Overview через SwimmReadDbContext (грант swimm_ro).
// Каскад сверху вниз: удалить серию — уйдут её части, регламенты и награды целиком.

internal sealed class CompetitionSeriesConfiguration : IEntityTypeConfiguration<CompetitionSeries>
{
    public void Configure(EntityTypeBuilder<CompetitionSeries> entity)
    {
        entity.ToTable("CompetitionSeries");
        entity.HasIndex(e => e.Name).IsUnique();

        entity.HasMany(e => e.Parts)
            .WithOne(p => p.Series)
            .HasForeignKey(p => p.SeriesId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasMany(e => e.Regulations)
            .WithOne(r => r.Series)
            .HasForeignKey(r => r.SeriesId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class CompetitionSeriesPartConfiguration : IEntityTypeConfiguration<CompetitionSeriesPart>
{
    public void Configure(EntityTypeBuilder<CompetitionSeriesPart> entity)
    {
        entity.ToTable("CompetitionSeriesParts");
        entity.HasIndex(e => new { e.SeriesId, e.Name }).IsUnique();
    }
}

internal sealed class SeriesRegulationConfiguration : IEntityTypeConfiguration<SeriesRegulation>
{
    public void Configure(EntityTypeBuilder<SeriesRegulation> entity)
    {
        entity.ToTable("SeriesRegulations");
        // Две версии с одной датой — выбор «действующей» стал бы случайным.
        entity.HasIndex(e => new { e.SeriesId, e.EffectiveFrom }).IsUnique();

        entity.HasMany(e => e.Awards)
            .WithOne(a => a.Regulation)
            .HasForeignKey(a => a.RegulationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class SeriesAwardConfiguration : IEntityTypeConfiguration<SeriesAward>
{
    public void Configure(EntityTypeBuilder<SeriesAward> entity)
    {
        entity.ToTable("SeriesAwards");

        // Cascade, а не Restrict: при удалении серии части и регламенты уходят одним каскадом,
        // и Restrict на части уронил бы его. Удалить часть — значит удалить и её награды.
        entity.HasOne(e => e.Part)
            .WithMany()
            .HasForeignKey(e => e.PartId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasMany(e => e.Bands)
            .WithOne(b => b.Award)
            .HasForeignKey(b => b.AwardId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasMany(e => e.Prizes)
            .WithOne(p => p.Award)
            .HasForeignKey(p => p.AwardId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class SeriesAwardBandConfiguration : IEntityTypeConfiguration<SeriesAwardBand>
{
    public void Configure(EntityTypeBuilder<SeriesAwardBand> entity) =>
        entity.ToTable("SeriesAwardBands");
}

internal sealed class SeriesAwardPrizeConfiguration : IEntityTypeConfiguration<SeriesAwardPrize>
{
    public void Configure(EntityTypeBuilder<SeriesAwardPrize> entity)
    {
        entity.ToTable("SeriesAwardPrizes");
        entity.HasIndex(e => new { e.AwardId, e.Place }).IsUnique();
    }
}
