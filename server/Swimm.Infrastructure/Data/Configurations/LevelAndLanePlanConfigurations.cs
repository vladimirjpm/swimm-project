using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Swimm.Domain.Entities;

namespace Swimm.Infrastructure.Data.Configurations;

// Уровни пловцов группы — оценка тренера, ПРИВАТНЫЕ данные, Sys_-таблица БЕЗ grant swimm_ro
// (docs/plans/lane-plans-plan.md). Rank НЕ уникален: список сохраняется целиком с новой
// нумерацией, и уникальный индекс падал бы на перестановке «1↔2» посреди UPDATE.
internal sealed class HubGroupLevelConfiguration : IEntityTypeConfiguration<HubGroupLevel>
{
    public void Configure(EntityTypeBuilder<HubGroupLevel> entity)
    {
        entity.ToTable("Sys_HubGroupLevels");
        entity.HasIndex(e => new { e.HubGroupId, e.Rank });
        // Цель составного FK уровня пловца: уровень обязан быть из той же группы.
        entity.HasAlternateKey(e => new { e.HubGroupId, e.Id });

        entity.HasOne(e => e.HubGroup)
            .WithMany()
            .HasForeignKey(e => e.HubGroupId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

// Уровень пловца: ключ (HubGroupId, SwimmerId), а не строка состава — клубная пересборка
// удаляет и заводит HubGroupMembers заново. Удаление уровня → пловец «без уровня» (cascade).
internal sealed class HubGroupSwimmerLevelConfiguration : IEntityTypeConfiguration<HubGroupSwimmerLevel>
{
    public void Configure(EntityTypeBuilder<HubGroupSwimmerLevel> entity)
    {
        entity.ToTable("Sys_HubGroupSwimmerLevels");
        entity.HasKey(e => new { e.HubGroupId, e.SwimmerId });
        entity.HasIndex(e => new { e.HubGroupId, e.LevelId });

        entity.HasOne(e => e.HubGroup)
            .WithMany()
            .HasForeignKey(e => e.HubGroupId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne(e => e.Swimmer)
            .WithMany()
            .HasForeignKey(e => e.SwimmerId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne(e => e.Level)
            .WithMany()
            .HasForeignKey(e => new { e.HubGroupId, e.LevelId })
            .HasPrincipalKey(l => new { l.HubGroupId, l.Id })
            .OnDelete(DeleteBehavior.Cascade);
    }
}

// Уровень аккаунта без пловца (Ш3.1) — пара к Sys_HubGroupSwimmerLevels, тот же составной
// FK на уровень этой же группы; удаление уровня → «без уровня» (cascade).
internal sealed class HubGroupAccountLevelConfiguration : IEntityTypeConfiguration<HubGroupAccountLevel>
{
    public void Configure(EntityTypeBuilder<HubGroupAccountLevel> entity)
    {
        entity.ToTable("Sys_HubGroupAccountLevels");
        entity.HasKey(e => new { e.HubGroupId, e.UserId });
        entity.HasIndex(e => new { e.HubGroupId, e.LevelId });
        entity.HasIndex(e => e.UserId);

        entity.HasOne(e => e.HubGroup)
            .WithMany()
            .HasForeignKey(e => e.HubGroupId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne(e => e.Level)
            .WithMany()
            .HasForeignKey(e => new { e.HubGroupId, e.LevelId })
            .HasPrincipalKey(l => new { l.HubGroupId, l.Id })
            .OnDelete(DeleteBehavior.Cascade);
    }
}

// План дорожек на дату — ПРИВАТНЫЕ данные группы, Sys_-таблицы БЕЗ grant swimm_ro
// (docs/plans/lane-plans-plan.md, L2). План — снимок: удаление уровня только снимает
// подпись с дорожки (SET NULL), уход пловца из состава план не трогает.
internal sealed class LanePlanConfiguration : IEntityTypeConfiguration<LanePlan>
{
    public void Configure(EntityTypeBuilder<LanePlan> entity)
    {
        entity.ToTable("Sys_LanePlans");
        entity.HasIndex(e => new { e.HubGroupId, e.Date }).IsUnique();

        entity.HasOne(e => e.HubGroup)
            .WithMany()
            .HasForeignKey(e => e.HubGroupId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne(e => e.CreatedBy)
            .WithMany()
            .HasForeignKey(e => e.CreatedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        entity.HasCheckConstraint("CK_LanePlans_Status", @"""Status"" IN ('draft', 'published')");
        entity.HasCheckConstraint("CK_LanePlans_LaneCount", @"""LaneCount"" BETWEEN 1 AND 12");
    }
}

internal sealed class LanePlanLaneConfiguration : IEntityTypeConfiguration<LanePlanLane>
{
    public void Configure(EntityTypeBuilder<LanePlanLane> entity)
    {
        entity.ToTable("Sys_LanePlanLanes");
        entity.HasKey(e => new { e.PlanId, e.LaneNo });
        entity.HasIndex(e => e.LevelId);

        entity.HasOne(e => e.Plan)
            .WithMany(p => p.Lanes)
            .HasForeignKey(e => e.PlanId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne(e => e.Level)
            .WithMany()
            .HasForeignKey(e => e.LevelId)
            .OnDelete(DeleteBehavior.SetNull);

        entity.HasCheckConstraint("CK_LanePlanLanes_LaneNo", @"""LaneNo"" >= 1");
    }
}

// Пловец плана. Нет FK (PlanId, LaneNo) → дорожка: при сокращении числа дорожек пловцов
// сначала переносят, и EF пришлось бы угадывать порядок команд; границы держит сервис.
internal sealed class LanePlanSwimmerConfiguration : IEntityTypeConfiguration<LanePlanSwimmer>
{
    public void Configure(EntityTypeBuilder<LanePlanSwimmer> entity)
    {
        entity.ToTable("Sys_LanePlanSwimmers");
        entity.HasKey(e => new { e.PlanId, e.SwimmerId });
        entity.HasIndex(e => e.SwimmerId);

        entity.HasOne(e => e.Plan)
            .WithMany(p => p.Swimmers)
            .HasForeignKey(e => e.PlanId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne(e => e.Swimmer)
            .WithMany()
            .HasForeignKey(e => e.SwimmerId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasCheckConstraint("CK_LanePlanSwimmers_LaneNo", @"""LaneNo"" IS NULL OR ""LaneNo"" >= 1");
    }
}
