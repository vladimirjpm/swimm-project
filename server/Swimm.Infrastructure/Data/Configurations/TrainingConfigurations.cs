using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Swimm.Domain.Entities;

namespace Swimm.Infrastructure.Data.Configurations;

// Тренировочная сессия — ПРИВАТНЫЕ данные группы, Sys_-таблица БЕЗ grant swimm_ro.
internal sealed class TrainingSessionConfiguration : IEntityTypeConfiguration<TrainingSession>
{
    public void Configure(EntityTypeBuilder<TrainingSession> entity)
    {
        entity.ToTable("Sys_TrainingSessions");

        entity.HasOne(e => e.HubGroup)
            .WithMany()
            .HasForeignKey(e => e.HubGroupId)
            .OnDelete(DeleteBehavior.Cascade);

        // Кто завёл тренировку кнопкой (Е1): удалили аккаунт — тренировка остаётся.
        entity.HasOne(e => e.CreatedBy)
            .WithMany()
            .HasForeignKey(e => e.CreatedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        // «Ask who's coming» (Р9): у всех прежних тренировок — false.
        entity.Property(e => e.AsksRsvp).HasDefaultValue(false);
    }
}

// Тренировочный повтор — ПРИВАТНЫЕ данные, Sys_-таблица БЕЗ grant swimm_ro.
// SwimmerId ссылается на публичную Swimmers (приватны времена, не сам пловец) → Restrict,
// чтобы удаление пловца не сносило тренировки молча.
internal sealed class TrainingResultConfiguration : IEntityTypeConfiguration<TrainingResult>
{
    public void Configure(EntityTypeBuilder<TrainingResult> entity)
    {
        entity.ToTable("Sys_TrainingResults");

        entity.HasOne(e => e.Session)
            .WithMany(s => s.Results)
            .HasForeignKey(e => e.SessionId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne(e => e.Swimmer)
            .WithMany()
            .HasForeignKey(e => e.SwimmerId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne(e => e.Style)
            .WithMany()
            .HasForeignKey(e => e.StyleId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

// Ответы «иду / не приду» на занятие — ПРИВАТНЫЕ, Sys_ без grant swimm_ro
// (docs/plans/entity-hero-roles-plan.md, Ш2). Занятие — пара «дата + начало слота»:
// расписание регулярное, строк занятий нет. Один ответ человека на занятие. Субъект —
// ровно одно из двух: аккаунт или пловец группы (Р73) — частичные UNIQUE, как у перерывов.
internal sealed class HubGroupTrainingRsvpConfiguration : IEntityTypeConfiguration<HubGroupTrainingRsvp>
{
    public void Configure(EntityTypeBuilder<HubGroupTrainingRsvp> entity)
    {
        entity.ToTable("Sys_HubGroupTrainingRsvps");
        // Имена явно: автоимя длиннее 63 символов, Postgres режет его, и два индекса спорили бы.
        entity.HasIndex(e => new { e.HubGroupId, e.SessionDate, e.SessionStart, e.UserId })
            .IsUnique()
            .HasFilter(@"""UserId"" IS NOT NULL")
            .HasDatabaseName("IX_Sys_HubGroupTrainingRsvps_Session_UserId");
        entity.HasIndex(e => new { e.HubGroupId, e.SessionDate, e.SessionStart, e.SwimmerId })
            .IsUnique()
            .HasFilter(@"""SwimmerId"" IS NOT NULL")
            .HasDatabaseName("IX_Sys_HubGroupTrainingRsvps_Session_SwimmerId");
        entity.HasIndex(e => e.UserId);
        entity.HasIndex(e => e.SwimmerId);

        entity.HasOne(e => e.HubGroup)
            .WithMany()
            .HasForeignKey(e => e.HubGroupId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne(e => e.SetBy)
            .WithMany()
            .HasForeignKey(e => e.SetByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        entity.HasOne(e => e.Swimmer)
            .WithMany()
            .HasForeignKey(e => e.SwimmerId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasCheckConstraint("CK_HubGroupTrainingRsvps_Subject",
            @"(""UserId"" IS NULL) <> (""SwimmerId"" IS NULL)");
        entity.HasCheckConstraint("CK_HubGroupTrainingRsvps_Answer", @"""Answer"" IN ('yes', 'maybe', 'no')");
        entity.HasCheckConstraint("CK_HubGroupTrainingRsvps_Note",
            @"""Note"" IS NULL OR ""Note"" IN ('late', 'first-hour', 'leaving-early')");
    }
}

// Отмена занятия (Р9) — ПРИВАТНЫЕ данные, Sys_ БЕЗ grant swimm_ro. Занятие — пара «дата +
// начало» (как у ответов); строка есть — занятие отменено, вернули — строку удаляют.
internal sealed class HubGroupTrainingCancellationConfiguration : IEntityTypeConfiguration<HubGroupTrainingCancellation>
{
    public void Configure(EntityTypeBuilder<HubGroupTrainingCancellation> entity)
    {
        entity.ToTable("Sys_HubGroupTrainingCancellations");
        entity.HasIndex(e => new { e.HubGroupId, e.Date, e.Start }).IsUnique();

        entity.HasOne(e => e.HubGroup)
            .WithMany()
            .HasForeignKey(e => e.HubGroupId)
            .OnDelete(DeleteBehavior.Cascade);

        // Удалили аккаунт тренера — отмена остаётся.
        entity.HasOne(e => e.CreatedBy)
            .WithMany()
            .HasForeignKey(e => e.CreatedByUserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

// «On break» (Ш3.1) — ПРИВАТНЫЕ данные, Sys_ БЕЗ grant swimm_ro. Субъект — ровно одно из
// двух (аккаунт или пловец); открытый перерыв у субъекта один — частичные UNIQUE.
internal sealed class HubGroupBreakConfiguration : IEntityTypeConfiguration<HubGroupBreak>
{
    public void Configure(EntityTypeBuilder<HubGroupBreak> entity)
    {
        entity.ToTable("Sys_HubGroupBreaks");
        entity.HasIndex(e => new { e.HubGroupId, e.UserId })
            .IsUnique()
            .HasFilter(@"""EndedAt"" IS NULL AND ""UserId"" IS NOT NULL");
        entity.HasIndex(e => new { e.HubGroupId, e.SwimmerId })
            .IsUnique()
            .HasFilter(@"""EndedAt"" IS NULL AND ""SwimmerId"" IS NOT NULL");

        entity.HasOne(e => e.HubGroup)
            .WithMany()
            .HasForeignKey(e => e.HubGroupId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne(e => e.Swimmer)
            .WithMany()
            .HasForeignKey(e => e.SwimmerId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne(e => e.SetBy)
            .WithMany()
            .HasForeignKey(e => e.SetByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        entity.HasCheckConstraint("CK_HubGroupBreaks_Subject",
            @"(""UserId"" IS NULL) <> (""SwimmerId"" IS NULL)");
        // «Не тренируется с группой» — бессрочно по смыслу: даты окончания у него нет.
        entity.Property(e => e.Kind).HasDefaultValue(HubGroupBreakKind.Break);
        entity.HasCheckConstraint("CK_HubGroupBreaks_Kind",
            @"""Kind"" IN ('break', 'not_training') AND (""Kind"" = 'break' OR ""Until"" IS NULL)");
    }
}

// «Когда открывал» виды таба Trainings (Т1б trainings-tab-redesign-plan) — ПРИВАТНЫЕ,
// Sys_ БЕЗ grant swimm_ro. Ключ (группа, аккаунт): у тренера-не-участника членства нет.
internal sealed class HubGroupUserSeenConfiguration : IEntityTypeConfiguration<HubGroupUserSeen>
{
    public void Configure(EntityTypeBuilder<HubGroupUserSeen> entity)
    {
        entity.ToTable("Sys_HubGroupUserSeen");
        entity.HasKey(e => new { e.HubGroupId, e.UserId });
        entity.HasIndex(e => e.UserId);

        entity.HasOne(e => e.HubGroup)
            .WithMany()
            .HasForeignKey(e => e.HubGroupId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
