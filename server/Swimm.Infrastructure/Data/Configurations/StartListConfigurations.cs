using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Swimm.Domain.Entities;

namespace Swimm.Infrastructure.Data.Configurations;

internal sealed class CompetitionEntryConfiguration : IEntityTypeConfiguration<CompetitionEntry>
{
    public void Configure(EntityTypeBuilder<CompetitionEntry> entity)
    {
        entity.ToTable("CompetitionEntries");

        // Дата дня — календарная, как CompetitionDate у результата: значения приходят из
        // разбора с Kind=Unspecified, и timestamptz на них падал бы. А вот HeatStartAt —
        // наоборот момент времени (Kind=Utc), и остаётся timestamptz по умолчанию.
        entity.Property(e => e.CompDate)
            .HasColumnType("timestamp without time zone");

        // Restrict, как у Results: заявка держит пловца и клуб от удаления.
        entity.HasOne(e => e.Swimmer)
            .WithMany()
            .HasForeignKey(e => e.SwimmerId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne(e => e.Club)
            .WithMany()
            .HasForeignKey(e => e.ClubId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne(e => e.Style)
            .WithMany()
            .HasForeignKey(e => e.StyleId)
            .OnDelete(DeleteBehavior.Restrict);

        // А вот справочная строка соревнования и результат могут исчезнуть при перезаливке
        // протокола — заявку это переживает, связь просто обнуляется. Иначе переимпорт
        // соревнования упирался бы в чужой FK.
        entity.HasOne(e => e.Competition)
            .WithMany()
            .HasForeignKey(e => e.CompetitionId)
            .OnDelete(DeleteBehavior.SetNull);

        entity.HasOne(e => e.Result)
            .WithMany()
            .HasForeignKey(e => e.ResultId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

// Sys_-таблица: журнал заборов — наша кухня, публичной роли не нужен.
internal sealed class StartListPullConfiguration : IEntityTypeConfiguration<StartListPull>
{
    public void Configure(EntityTypeBuilder<StartListPull> entity)
    {
        entity.ToTable("Sys_StartListPulls");
    }
}

// Источники стартового протокола. ПУБЛИЧНАЯ таблица (без Sys_): её читает овервью
// соревнования под swimm_ro. Каскад от Competition: привязка без дня бессмысленна.
internal sealed class CompetitionSourceConfiguration : IEntityTypeConfiguration<CompetitionSource>
{
    public void Configure(EntityTypeBuilder<CompetitionSource> entity)
    {
        entity.ToTable("CompetitionSources");

        // Календарная дата протокола, как CompDate у заявки — приходит с Kind=Unspecified.
        entity.Property(e => e.SourceDate)
            .HasColumnType("timestamp without time zone");

        entity.HasOne(e => e.Competition)
            .WithMany()
            .HasForeignKey(e => e.CompetitionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

// Справка о предстоящем старте. ПУБЛИЧНАЯ (без Sys_): из неё таб Start list считает
// «во сколько приезжать», а публичный путь ходит под swimm_ro. Ключ — OrgCompId, без
// FK на Competitions: справочной строки у будущего соревнования ещё нет (§3.1 плана).
internal sealed class CompetitionMeetInfoConfiguration : IEntityTypeConfiguration<CompetitionMeetInfo>
{
    public void Configure(EntityTypeBuilder<CompetitionMeetInfo> entity)
    {
        entity.ToTable("CompetitionMeetInfos");
        entity.HasKey(e => e.OrgCompId);
        entity.Property(e => e.OrgCompId).ValueGeneratedNever();
        entity.Property(e => e.RegulationUrl).HasMaxLength(500);

        // Действующее значение считается на лету — колонки под него нет.
        entity.Ignore(e => e.ChampionshipEffective);
    }
}

internal sealed class CompetitionWarmUpConfiguration : IEntityTypeConfiguration<CompetitionWarmUp>
{
    public void Configure(EntityTypeBuilder<CompetitionWarmUp> entity)
    {
        entity.ToTable("CompetitionWarmUps");

        // Ключ — «соревнование + день»: двух разминок в один день не бывает, и суррогатный
        // id тут только позволил бы им завестись.
        entity.HasKey(e => new { e.OrgCompId, e.Date });

        // Календарный день, как CompDate у заявки: Kind=Unspecified, timestamptz упал бы.
        entity.Property(e => e.Date).HasColumnType("timestamp without time zone");

        entity.HasOne(e => e.MeetInfo)
            .WithMany(m => m.WarmUps)
            .HasForeignKey(e => e.OrgCompId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
