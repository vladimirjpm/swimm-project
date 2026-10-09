using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Swimm.Domain.Entities;

namespace Swimm.Infrastructure.Data.Configurations;

internal sealed class ResultRecordConfiguration : IEntityTypeConfiguration<ResultRecord>
{
    public void Configure(EntityTypeBuilder<ResultRecord> entity)
    {
        entity.ToTable("Results");

        // CompetitionDate — календарная дата соревнования, а не момент времени с таймзоной.
        // Маппим в timestamp without time zone: значения приходят из парсинга с Kind=Unspecified,
        // и timestamptz (требующий Kind=Utc) на них падал бы при записи/фильтрации.
        entity.Property(r => r.CompetitionDate)
            .HasColumnType("timestamp without time zone");

        entity.HasOne(r => r.Competition)
            .WithMany()
            .HasForeignKey(r => r.CompetitionId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne(r => r.Swimmer)
            .WithMany()
            .HasForeignKey(r => r.SwimmerId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne(r => r.Club)
            .WithMany()
            .HasForeignKey(r => r.ClubId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne(r => r.Style)
            .WithMany()
            .HasForeignKey(r => r.StyleId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne(r => r.Relay)
            .WithMany()
            .HasForeignKey(r => r.RelayId)
            .OnDelete(DeleteBehavior.SetNull);

        entity.HasOne(r => r.Gallery)
            .WithMany()
            .HasForeignKey(r => r.GalleryId)
            .OnDelete(DeleteBehavior.SetNull);

        entity.HasOne(r => r.Country)
            .WithMany()
            .HasForeignKey(r => r.CountryId)
            .OnDelete(DeleteBehavior.SetNull);

        // Помеченных строк единицы на сотни тысяч — частичный индекс, чтобы выборка
        // «что подозрительно» не делала seq scan и не раздувала индекс пустыми ключами.
        entity.HasIndex(r => r.SuspectReason)
            .HasDatabaseName("IX_Results_SuspectReason")
            .HasFilter("\"SuspectReason\" IS NOT NULL");

        entity.HasCheckConstraint("CK_Results_Heat_NonNegative", "\"Heat\" >= 0");
        entity.HasCheckConstraint("CK_Results_Lane_NonNegative", "\"Lane\" >= 0");
        entity.HasCheckConstraint("CK_Results_InternationalPoints_NonNegative", "\"InternationalPoints\" >= 0");
        entity.HasCheckConstraint("CK_Results_Position_PositiveOrNull", "\"Position\" IS NULL OR \"Position\" > 0");
        entity.HasCheckConstraint("CK_Results_PositionAgeGroup_PositiveOrNull", "\"PositionAgeGroup\" IS NULL OR \"PositionAgeGroup\" > 0");
    }
}
