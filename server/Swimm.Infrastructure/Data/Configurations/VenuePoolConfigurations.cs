using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Swimm.Domain.Entities;

namespace Swimm.Infrastructure.Data.Configurations;

// Справочник «место → длина бассейна» (П4, docs/plans/prod-auto-pull-plan.md §4.4) — Sys_-таблицы,
// БЕЗ гранта swimm_ro; обе в seed-tables.txt (едут «прод → локально», триггер версии в миграции).
internal sealed class VenuePoolConfiguration : IEntityTypeConfiguration<VenuePool>
{
    public void Configure(EntityTypeBuilder<VenuePool> entity)
    {
        // Длина есть ровно у однозначного места; у двусмысленного её нет (выбирает человек).
        entity.ToTable("Sys_VenuePools", t => t.HasCheckConstraint(
            "CK_Sys_VenuePools_PoolType",
            @"(""IsAmbiguous"" AND ""PoolType"" IS NULL) OR (NOT ""IsAmbiguous"" AND ""PoolType"" IS NOT NULL AND ""PoolType"" IN ('25m', '50m'))"));
        entity.HasIndex(e => e.Name).IsUnique();
    }
}

internal sealed class VenuePoolAliasConfiguration : IEntityTypeConfiguration<VenuePoolAlias>
{
    public void Configure(EntityTypeBuilder<VenuePoolAlias> entity)
    {
        entity.ToTable("Sys_VenuePoolAliases");
        // Одно написание — одно место во всём справочнике.
        entity.HasIndex(e => e.NormalizedAlias).IsUnique();
        entity.HasOne(e => e.VenuePool).WithMany(v => v.Aliases)
            .HasForeignKey(e => e.VenuePoolId).OnDelete(DeleteBehavior.Cascade);
    }
}
