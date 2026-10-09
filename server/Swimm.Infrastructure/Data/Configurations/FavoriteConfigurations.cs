using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Swimm.Domain.Entities;

namespace Swimm.Infrastructure.Data.Configurations;

internal sealed class UserFavoriteConfiguration : IEntityTypeConfiguration<UserFavorite>
{
    public void Configure(EntityTypeBuilder<UserFavorite> entity)
    {
        entity.ToTable("Sys_UserFavorites");

        entity.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne(e => e.Swimmer)
            .WithMany()
            .HasForeignKey(e => e.SwimmerId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne(e => e.Club)
            .WithMany()
            .HasForeignKey(e => e.ClubId)
            .OnDelete(DeleteBehavior.Restrict);

        // Дискриминатор: swimmer → SwimmerId NOT NULL, ClubId NULL; club → обратно.
        entity.HasCheckConstraint(
            "CK_UserFav_TargetType",
            @"(""TargetType"" = 'swimmer' AND ""SwimmerId"" IS NOT NULL AND ""ClubId"" IS NULL) OR " +
            @"(""TargetType"" = 'club'    AND ""ClubId""    IS NOT NULL AND ""SwimmerId"" IS NULL)");

        // Partial unique indexes создаются вручную в миграции через migrationBuilder.Sql
        // (UX_UserFav_OnePrimary, UX_UserFav_Swimmer, UX_UserFav_Club).
    }
}

// Персональный план на соревнование (Т3). Sys_ и БЕЗ гранта swimm_ro: заявки публичны,
// а «за кем я слежу и приду ли я» — нет (§8 плана стартового протокола).
internal sealed class UserStartListPlanConfiguration : IEntityTypeConfiguration<UserStartListPlan>
{
    public void Configure(EntityTypeBuilder<UserStartListPlan> entity)
    {
        entity.ToTable("Sys_UserStartListPlans");

        // Один план на пару «пользователь + соревнование»: состав пишется целиком,
        // и второй строке взяться неоткуда — кроме как из гонки двух вкладок.
        entity.HasIndex(e => new { e.UserId, e.OrgCompId }).IsUnique();

        entity.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
