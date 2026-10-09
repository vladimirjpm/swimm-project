using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Swimm.Domain.Entities;

namespace Swimm.Infrastructure.Data.Configurations;

internal sealed class CompetitionConfiguration : IEntityTypeConfiguration<Competition>
{
    public void Configure(EntityTypeBuilder<Competition> entity)
    {
        entity.ToTable("Competitions");
        entity.HasIndex(e => new { e.Name, e.Date, e.PoolType }).IsUnique();

        // Уникальность OrgCompId обеспечена реальным UNIQUE constraint'ом (raw SQL в миграции),
        // а не EF alternate key — тот требует NOT NULL, а OrgCompId должен быть nullable
        // (у старых соревнований его пока нет). См. CompetitionResultUrl.

        // Многодневные соревнования: день → событие. SetNull — удаление события не трогает дни.
        entity.HasOne(e => e.Event)
            .WithMany(ev => ev.Days)
            .HasForeignKey(e => e.EventId)
            .OnDelete(DeleteBehavior.SetNull);

        // Правила очков (Э0): удаление используемого правила должно падать (Restrict),
        // а не молча переводить соревнования на другую шкалу (SetNull).
        entity.HasOne(e => e.PointRuleClubs)
            .WithMany()
            .HasForeignKey(e => e.PointRuleClubsId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne(e => e.PointRuleSwimmers)
            .WithMany()
            .HasForeignKey(e => e.PointRuleSwimmersId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CompetitionEventConfiguration : IEntityTypeConfiguration<CompetitionEvent>
{
    public void Configure(EntityTypeBuilder<CompetitionEvent> entity)
    {
        entity.ToTable("CompetitionEvents");
        // Штамп сайта на событии (Д2). Индекс НЕ уникальный: одному событию могут
        // соответствовать две записи Discovery (6621 и 6622 → один и тот же протокол).
        entity.HasIndex(e => e.OrgCompId);
    }
}

internal sealed class CompetitionResultUrlConfiguration : IEntityTypeConfiguration<CompetitionResultUrl>
{
    public void Configure(EntityTypeBuilder<CompetitionResultUrl> entity)
    {
        entity.ToTable("CompetitionResultUrls");
        entity.HasIndex(e => new { e.OrgCompId, e.Culture }).IsUnique();

        // Связь с Competition идёт по OrgCompId, не Id — намеренно не смоделирована как EF
        // relationship (см. комментарий у Competition.OrgCompId). FK создан raw SQL в миграции.
    }
}

internal sealed class ClubConfiguration : IEntityTypeConfiguration<Club>
{
    public void Configure(EntityTypeBuilder<Club> entity)
    {
        entity.ToTable("Clubs");

        entity.HasOne(e => e.Country)
            .WithMany()
            .HasForeignKey(e => e.CountryId)
            .OnDelete(DeleteBehavior.SetNull);

        // Мягкий merge: склеенный клуб не удаляется, а ссылается на приёмника.
        // Restrict — приёмника нельзя удалить, пока на него ссылаются склеенные:
        // иначе ссылка повисла бы и /clubs/{старый id} снова отдавал бы 404.
        entity.HasOne(e => e.MergedInto)
            .WithMany()
            .HasForeignKey(e => e.MergedIntoId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasIndex(e => e.MergedIntoId);
    }
}

internal sealed class SwimmerConfiguration : IEntityTypeConfiguration<Swimmer>
{
    public void Configure(EntityTypeBuilder<Swimmer> entity)
    {
        entity.ToTable("Swimmers");

        // Существующие/новые isr-пловцы = 'isr' по умолчанию; local проставляет сидер явно.
        entity.Property(e => e.Origin).HasDefaultValue("isr");

        // Пол человека — только male/female или неизвестен (NULL). Старые «M»/«F» от сидера
        // тренировок (хвост §5 records-relays-plan, 22.09.2026) переведены миграцией
        // SwimmerGenderMaleFemale; ограничение не даёт им вернуться.
        entity.ToTable(t => t.HasCheckConstraint("CK_Swimmers_Gender",
            "\"Gender\" IS NULL OR \"Gender\" IN ('male', 'female')"));

        entity.HasOne(e => e.Club)
            .WithMany()
            .HasForeignKey(e => e.ClubId)
            .OnDelete(DeleteBehavior.SetNull);

        // Пловец группы (Р71) живёт и умирает с группой: SetNull сделал бы его публичным.
        entity.HasOne(e => e.PrivateHubGroup)
            .WithMany()
            .HasForeignKey(e => e.PrivateHubGroupId)
            .OnDelete(DeleteBehavior.Cascade);
        entity.HasIndex(e => e.PrivateHubGroupId)
            .HasFilter("\"PrivateHubGroupId\" IS NOT NULL");

        entity.HasOne(e => e.Country)
            .WithMany()
            .HasForeignKey(e => e.CountryId)
            .OnDelete(DeleteBehavior.SetNull);

        // Привязка к loglig.com уникальна только среди заполненных значений — частичный
        // индекс (Npgsql по умолчанию делает индекс по nullable-колонке полным, не partial).
        entity.HasIndex(e => e.LogligId)
            .IsUnique()
            .HasFilter("\"LogligId\" IS NOT NULL");
    }
}

internal sealed class CountryConfiguration : IEntityTypeConfiguration<Country>
{
    public void Configure(EntityTypeBuilder<Country> entity)
    {
        entity.ToTable("Countries");
    }
}

internal sealed class RelayConfiguration : IEntityTypeConfiguration<Relay>
{
    public void Configure(EntityTypeBuilder<Relay> entity)
    {
        entity.ToTable("Relays");
    }
}

// Ноги эстафеты (публичная reference-таблица, грант swimm_ro в миграции) —
// структурное членство «эстафета → пловцы», см. RelayMember.
internal sealed class RelayMemberConfiguration : IEntityTypeConfiguration<RelayMember>
{
    public void Configure(EntityTypeBuilder<RelayMember> entity)
    {
        entity.ToTable("RelayMembers");

        entity.HasOne(e => e.Relay)
            .WithMany(r => r.Members)
            .HasForeignKey(e => e.RelayId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne(e => e.Swimmer)
            .WithMany()
            .HasForeignKey(e => e.SwimmerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

// Материализованный клубный зачёт соревнования (страница клуба, Фаза 10).
// Cascade с обеих сторон намеренно: строка зачёта — производная, без своего
// соревнования или клуба смысла не имеет и должна уходить вместе с ними.
internal sealed class ClubCompetitionStandingConfiguration : IEntityTypeConfiguration<ClubCompetitionStanding>
{
    public void Configure(EntityTypeBuilder<ClubCompetitionStanding> entity)
    {
        entity.ToTable("ClubCompetitionStandings");

        entity.HasOne(e => e.Competition)
            .WithMany()
            .HasForeignKey(e => e.CompetitionId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne(e => e.Club)
            .WithMany()
            .HasForeignKey(e => e.ClubId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class CompetitionNoteConfiguration : IEntityTypeConfiguration<CompetitionNote>
{
    public void Configure(EntityTypeBuilder<CompetitionNote> entity)
    {
        entity.ToTable("CompetitionNotes");

        entity.HasOne(e => e.Competition)
            .WithMany()
            .HasForeignKey(e => e.CompetitionId)
            .OnDelete(DeleteBehavior.Cascade);

        // Переводы живут только вместе с заметкой.
        entity.HasMany(e => e.Texts)
            .WithOne(t => t.Note)
            .HasForeignKey(t => t.NoteId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class CompetitionNoteTextConfiguration : IEntityTypeConfiguration<CompetitionNoteText>
{
    public void Configure(EntityTypeBuilder<CompetitionNoteText> entity)
    {
    entity.ToTable("CompetitionNoteTexts");
    }
}

internal sealed class StyleConfiguration : IEntityTypeConfiguration<Style>
{
    public void Configure(EntityTypeBuilder<Style> entity)
    {
        entity.ToTable("Styles");
        entity.HasData(
            new Style { Id = 1, Name = "freestyle" },
            new Style { Id = 2, Name = "backstroke" },
            new Style { Id = 3, Name = "breaststroke" },
            new Style { Id = 4, Name = "butterfly" },
            new Style { Id = 5, Name = "individual_medley" },
            new Style { Id = 6, Name = "medley_relay" },
            new Style { Id = 7, Name = "free_relay" }
        );
    }
}
