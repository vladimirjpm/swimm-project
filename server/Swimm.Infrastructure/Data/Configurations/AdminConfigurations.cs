using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Swimm.Domain.Entities;

namespace Swimm.Infrastructure.Data.Configurations;

// Аудит ручных мутаций админки (фаза 7.4) — Sys_-таблица, БЕЗ grant swimm_ro.
// Actor денормализован (без FK на Sys_AppUsers), чтобы запись переживала удаление юзера.
internal sealed class AdminAuditConfiguration : IEntityTypeConfiguration<AdminAudit>
{
    public void Configure(EntityTypeBuilder<AdminAudit> entity)
    {
        entity.ToTable("Sys_AdminAudit");
        entity.HasIndex(e => e.CreatedAt);                       // лента «новые сверху»
        entity.HasIndex(e => new { e.EntityType, e.EntityId });  // история по объекту
        entity.HasIndex(e => e.Action);                          // фильтр по типу действия
    }
}

// «Развязанные» пары дедупа (админ подтвердил «не дубли») — Sys_-таблица, БЕЗ grant swimm_ro.
internal sealed class DedupIgnoredPairConfiguration : IEntityTypeConfiguration<DedupIgnoredPair>
{
    public void Configure(EntityTypeBuilder<DedupIgnoredPair> entity)
    {
        entity.ToTable("Sys_DedupIgnoredPairs");
        entity.HasIndex(e => new { e.EntityType, e.IdA, e.IdB }).IsUnique();
    }
}
