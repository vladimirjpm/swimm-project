using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Swimm.Domain.Entities;

namespace Swimm.Infrastructure.Data.Configurations;

// Напоминания через Web Push (docs/plans/web-push-reminders-plan.md §5). Всё — ЛИЧНЫЕ данные:
// Sys_ БЕЗ grant swimm_ro и НЕ в seed-tables (подписка привязана к устройству человека).

internal sealed class UserPushSubscriptionConfiguration : IEntityTypeConfiguration<UserPushSubscription>
{
    public void Configure(EntityTypeBuilder<UserPushSubscription> entity)
    {
        entity.ToTable("Sys_UserPushSubscriptions");

        // Один браузер — одна строка; повторное включение — upsert по адресу.
        entity.HasIndex(e => e.Endpoint).IsUnique();
        entity.HasIndex(e => e.UserId);

        entity.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class UserNotificationSettingsConfiguration : IEntityTypeConfiguration<UserNotificationSettings>
{
    public void Configure(EntityTypeBuilder<UserNotificationSettings> entity)
    {
        entity.ToTable("Sys_UserNotificationSettings");

        entity.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasCheckConstraint("CK_Sys_UserNotificationSettings_LeadKind",
            @"""LeadKind"" IN ('1h', '2h', 'evening')");
    }
}

internal sealed class HubGroupReminderMuteConfiguration : IEntityTypeConfiguration<HubGroupReminderMute>
{
    public void Configure(EntityTypeBuilder<HubGroupReminderMute> entity)
    {
        entity.ToTable("Sys_HubGroupReminderMutes");
        entity.HasKey(e => new { e.UserId, e.HubGroupId });

        entity.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne(e => e.HubGroup)
            .WithMany()
            .HasForeignKey(e => e.HubGroupId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ReminderDeliveryConfiguration : IEntityTypeConfiguration<ReminderDelivery>
{
    public void Configure(EntityTypeBuilder<ReminderDelivery> entity)
    {
        entity.ToTable("Sys_ReminderDeliveries");

        // ЗАЩИТА ОТ ДУБЛЯ (§4 п.2): вставка до отправки; не вставилось — уже отправлено.
        entity.HasIndex(e => new { e.UserId, e.HubGroupId, e.SessionDate, e.SessionStart, e.Kind })
            .IsUnique()
            .HasDatabaseName("UX_Sys_ReminderDeliveries_Session");
        // Чистка старого журнала и карточка /Admin/Jobs — по времени.
        entity.HasIndex(e => e.CreatedAt);

        entity.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne(e => e.HubGroup)
            .WithMany()
            .HasForeignKey(e => e.HubGroupId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasCheckConstraint("CK_Sys_ReminderDeliveries_Kind",
            @"""Kind"" IN ('reminder', 'cancelled')");
        entity.HasCheckConstraint("CK_Sys_ReminderDeliveries_Channel",
            @"""Channel"" IN ('push', 'email')");
        entity.HasCheckConstraint("CK_Sys_ReminderDeliveries_Result",
            @"""Result"" IN ('pending', 'sent', 'gone', 'failed')");
    }
}
