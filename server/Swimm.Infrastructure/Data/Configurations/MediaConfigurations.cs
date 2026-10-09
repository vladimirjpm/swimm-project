using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Swimm.Domain.Entities;

namespace Swimm.Infrastructure.Data.Configurations;

internal sealed class UserMediaConfiguration : IEntityTypeConfiguration<UserMedia>
{
    public void Configure(EntityTypeBuilder<UserMedia> entity)
    {
        entity.ToTable("Sys_UserMedia");

        entity.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne(e => e.Swimmer)
            .WithMany()
            .HasForeignKey(e => e.SwimmerId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne(e => e.ResultRecord)
            .WithMany()
            .HasForeignKey(e => e.ResultId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne(e => e.Competition)
            .WithMany()
            .HasForeignKey(e => e.CompetitionId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasCheckConstraint(
            "CK_UserMedia_Level",
            @"""Level"" IN ('swimmer', 'competition', 'result')");

        entity.HasCheckConstraint(
            "CK_UserMedia_Visibility",
            @"""Visibility"" IN ('private', 'public')");

        // Жалобы «Report» (Р62): null — обычное медиа; значения — MediaReportRules.
        entity.HasCheckConstraint(
            "CK_UserMedia_ModerationState",
            @"""ModerationState"" IS NULL OR ""ModerationState"" IN ('under_review', 'removed')");
    }
}

// Публикации личного медиа в группы (этап 2 media-visibility-model) — заявки/решения,
// таблица с личными данными, БЕЗ grant swimm_ro (читается сервисами через RW-контекст).
internal sealed class UserMediaPublicationConfiguration : IEntityTypeConfiguration<UserMediaPublication>
{
    public void Configure(EntityTypeBuilder<UserMediaPublication> entity)
    {
        entity.ToTable("Sys_UserMediaPublications");

        entity.HasOne(e => e.Media)
            .WithMany()
            .HasForeignKey(e => e.UserMediaId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne(e => e.HubGroup)
            .WithMany()
            .HasForeignKey(e => e.HubGroupId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne(e => e.Club)
            .WithMany()
            .HasForeignKey(e => e.ClubId)
            .OnDelete(DeleteBehavior.Cascade);

        // «Одна публикация на пару (медиа, цель)» — ДВА частичных индекса, а не один общий
        // по (UserMediaId, TargetType, …): в Postgres NULL'ы в уникальном индексе считаются
        // различными, и общий индекс не удержал бы уникальность там, где вторая колонка
        // цели пуста.
        entity.HasIndex(e => new { e.UserMediaId, e.HubGroupId })
            .IsUnique()
            .HasFilter("\"HubGroupId\" IS NOT NULL");

        entity.HasIndex(e => new { e.UserMediaId, e.ClubId })
            .IsUnique()
            .HasFilter("\"ClubId\" IS NOT NULL");

        entity.HasOne(e => e.DecidedBy)
            .WithMany()
            .HasForeignKey(e => e.DecidedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        entity.HasCheckConstraint(
            "CK_UserMediaPublications_Level",
            @"""Level"" IN ('members', 'public')");

        entity.HasCheckConstraint(
            "CK_UserMediaPublications_Status",
            @"""Status"" IN ('pending', 'approved', 'rejected')");

        entity.HasCheckConstraint(
            "CK_UserMediaPublications_Target",
            @"""TargetType"" IN ('group', 'club')");

        // Цель ровно одна и совпадает с типом. Без этого «полиморфная цель» держалась бы
        // на честном слове кода: строка с двумя заполненными FK (или с нулём) прошла бы в
        // БД и разъехалась бы с выборками, которые фильтруют по одной из колонок.
        entity.HasCheckConstraint(
            "CK_UserMediaPublications_TargetShape",
            @"(""TargetType"" = 'group' AND ""HubGroupId"" IS NOT NULL AND ""ClubId"" IS NULL)
                  OR (""TargetType"" = 'club' AND ""ClubId"" IS NOT NULL AND ""HubGroupId"" IS NULL)");
    }
}

// Реакции пользователей (❤ на медиа / 🎉 на заплыв) — Sys_-таблица БЕЗ grant swimm_ro:
// публичные счётчики отдаются агрегатами через RW-контекст, сырые строки наружу не идут.
internal sealed class UserReactionConfiguration : IEntityTypeConfiguration<UserReaction>
{
    public void Configure(EntityTypeBuilder<UserReaction> entity)
    {
        entity.ToTable("Sys_UserReactions");

        entity.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne(e => e.Media)
            .WithMany()
            .HasForeignKey(e => e.MediaId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne(e => e.ResultRecord)
            .WithMany()
            .HasForeignKey(e => e.ResultId)
            .OnDelete(DeleteBehavior.Cascade);

        // Дискриминатор: like → MediaId NOT NULL, congrats → ResultId NOT NULL.
        entity.HasCheckConstraint(
            "CK_UserReactions_Kind",
            @"(""Kind"" = 'like'     AND ""MediaId""  IS NOT NULL AND ""ResultId"" IS NULL) OR " +
            @"(""Kind"" = 'congrats' AND ""ResultId"" IS NOT NULL AND ""MediaId""  IS NULL)");

        // Partial unique indexes (одна реакция на юзера+цель) + счётные индексы по цели —
        // вручную в миграции через migrationBuilder.Sql (UX_UserReactions_Like/Congrats).
    }
}

// Жалобы «Report» на медиа (Р62). Sys_: кто пожаловался, видит только админ сайта.
internal sealed class MediaReportConfiguration : IEntityTypeConfiguration<MediaReport>
{
    public void Configure(EntityTypeBuilder<MediaReport> entity)
    {
        entity.ToTable("Sys_MediaReports");

        entity.HasOne(e => e.Media)
            .WithMany()
            .HasForeignKey(e => e.UserMediaId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne(e => e.Reporter)
            .WithMany()
            .HasForeignKey(e => e.ReporterUserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Одна жалоба на медиа от аккаунта: иначе один человек добивал бы порог сам.
        entity.HasIndex(e => new { e.UserMediaId, e.ReporterUserId }).IsUnique();
        // Очередь админки и подсчёт порога: открытые жалобы медиа.
        entity.HasIndex(e => new { e.Status, e.UserMediaId });

        entity.HasCheckConstraint(
            "CK_MediaReports_Reason",
            @"""Reason"" IN ('wrong_swimmer', 'inappropriate', 'spam', 'privacy', 'other')");
        entity.HasCheckConstraint(
            "CK_MediaReports_Status",
            @"""Status"" IN ('open', 'kept', 'removed')");
        // «Other» без текста — не жалоба, а случайный клик.
        entity.HasCheckConstraint(
            "CK_MediaReports_OtherNeedsComment",
            @"""Reason"" <> 'other' OR (""Comment"" IS NOT NULL AND length(btrim(""Comment"")) > 0)");
    }
}
