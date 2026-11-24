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

// Журнал смен клуба в карточке пловца (Р95, docs/plans/swimmer-club-change-plan.md Ф1) —
// Sys_-таблица, БЕЗ grant swimm_ro и не в seed-tables.txt. FK нет: запись переживает склейку.
internal sealed class SwimmerClubChangeConfiguration : IEntityTypeConfiguration<SwimmerClubChange>
{
    public void Configure(EntityTypeBuilder<SwimmerClubChange> entity)
    {
        entity.ToTable("Sys_SwimmerClubChanges", t => t.HasCheckConstraint(
            "CK_Sys_SwimmerClubChanges_Source",
            @"""Source"" IN ('import', 'cli', 'admin', 'loglig')"));
        entity.HasIndex(e => e.ChangedAt);                    // карточка дашборда «за 7 дней»
        entity.HasIndex(e => new { e.SwimmerId, e.ChangedAt }); // история пловца
    }
}

// Кандидаты на смену клуба, ждущие подтверждения loglig (Р98, §6 swimmer-club-change-plan) —
// Sys_-таблица, БЕЗ grant swimm_ro. Один кандидат на пловца (ключ SwimmerId), FK нет.
internal sealed class SwimmerClubCandidateConfiguration : IEntityTypeConfiguration<SwimmerClubCandidate>
{
    public void Configure(EntityTypeBuilder<SwimmerClubCandidate> entity)
    {
        entity.ToTable("Sys_SwimmerClubCandidates", t =>
        {
            t.HasCheckConstraint("CK_Sys_SwimmerClubCandidates_Source", @"""Source"" IN ('import', 'cli')");
            t.HasCheckConstraint("CK_Sys_SwimmerClubCandidates_Verdict",
                @"""Verdict"" IS NULL OR ""Verdict"" IN ('confirmed', 'old', 'archive', 'other', 'no-loglig-id', 'unavailable')");
        });
        entity.HasIndex(e => e.Verdict); // дашборд: ждущие по причинам
    }
}

// Последний ответ карточки loglig про клуб пловца (Р112) — Sys_-таблица, БЕЗ grant swimm_ro,
// ключ SwimmerId, FK нет. Читается пачкой по id пловцов — индекс не нужен.
internal sealed class SwimmerLogligClubConfiguration : IEntityTypeConfiguration<SwimmerLogligClub>
{
    public void Configure(EntityTypeBuilder<SwimmerLogligClub> entity) =>
        entity.ToTable("Sys_SwimmerLogligClubs");
}

// Игроки loglig, встреченные на страницах дисциплин (Р122) — Sys_-таблица, БЕЗ grant swimm_ro,
// ключ — id игрока на loglig, FK нет. Читается пачкой по id — индекс не нужен.
internal sealed class LogligPlayerConfiguration : IEntityTypeConfiguration<LogligPlayer>
{
    public void Configure(EntityTypeBuilder<LogligPlayer> entity) =>
        entity.ToTable("Sys_LogligPlayers");
}
