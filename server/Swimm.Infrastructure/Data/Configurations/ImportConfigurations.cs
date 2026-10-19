using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Swimm.Domain.Entities;

namespace Swimm.Infrastructure.Data.Configurations;

// «Входящие» автозабора isr.org.il (фаза 6) — приватная Sys_-таблица, без grant swimm_ro.
internal sealed class DiscoveredCompetitionConfiguration : IEntityTypeConfiguration<DiscoveredCompetition>
{
    public void Configure(EntityTypeBuilder<DiscoveredCompetition> entity)
    {
        entity.ToTable("Sys_DiscoveredCompetitions");
    }
}

internal sealed class ImportHistoryConfiguration : IEntityTypeConfiguration<ImportHistory>
{
    public void Configure(EntityTypeBuilder<ImportHistory> entity)
    {
        entity.ToTable("Sys_ImportHistory");
        entity.HasIndex(e => e.CompetitionId);
        entity.HasIndex(e => e.ImportDate);

        entity.HasOne(e => e.Competition)
            .WithMany()
            .HasForeignKey(e => e.CompetitionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

// Реестр проверок данных (Д3).
internal sealed class DataCheckRunConfiguration : IEntityTypeConfiguration<DataCheckRun>
{
    public void Configure(EntityTypeBuilder<DataCheckRun> entity)
    {
        entity.ToTable("Sys_DataCheckRuns");
        entity.HasIndex(e => e.StartedAt);
    }
}

// Итог последнего прогона по каждой проверке — источник счётчиков дашборда.
internal sealed class DataCheckStateConfiguration : IEntityTypeConfiguration<DataCheckState>
{
    public void Configure(EntityTypeBuilder<DataCheckState> entity)
    {
    entity.ToTable("Sys_DataCheckStates");
    }
}

internal sealed class DataCheckFindingConfiguration : IEntityTypeConfiguration<DataCheckFinding>
{
    public void Configure(EntityTypeBuilder<DataCheckFinding> entity)
    {
        entity.ToTable("Sys_DataCheckFindings");
        // Ключ находки между прогонами — по нему её узнают и сохраняют решение.
        entity.HasIndex(e => new { e.CheckId, e.EntityType, e.EntityId });
        // Открытые и принятые — то, что спрашивают у таблицы всегда; починенных со
        // временем станет на порядки больше, поэтому частичный индекс.
        entity.HasIndex(e => e.Resolution)
            .HasFilter("\"Resolution\" IS NULL OR \"Resolution\" = 'accepted'")
            .HasDatabaseName("IX_Sys_DataCheckFindings_Open");
    }
}

// Сверка импорта (Д1): журнал «файл обещал N, в БД оказалось M» по каждому заплыву.
internal sealed class ImportReconciliationConfiguration : IEntityTypeConfiguration<ImportReconciliation>
{
    public void Configure(EntityTypeBuilder<ImportReconciliation> entity)
    {
        entity.ToTable("Sys_ImportReconciliation");
        entity.HasIndex(e => e.CompetitionId);
        entity.HasIndex(e => e.ImportedAt);
        // Частичный индекс: расхождения — то, что спрашивают у таблицы чаще всего,
        // а их на порядки меньше, чем успешных строк сверки.
        entity.HasIndex(e => e.Status)
            .HasFilter("\"Status\" = 'mismatch'")
            .HasDatabaseName("IX_Sys_ImportReconciliation_Mismatch");

        entity.HasOne(e => e.Competition)
            .WithMany()
            .HasForeignKey(e => e.CompetitionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

// Журнал запусков фоновых задач по расписанию (В12). Sys_: служебное, гранта swimm_ro нет; в
// прод-наполнение и синхронизацию не едет — у каждой базы свой журнал (seed-tables.txt).
internal sealed class JobRunConfiguration : IEntityTypeConfiguration<JobRun>
{
    public void Configure(EntityTypeBuilder<JobRun> entity)
    {
        entity.ToTable("Sys_JobRuns");
        entity.HasKey(e => e.Name);
        entity.Property(e => e.Name).HasMaxLength(100);
        entity.Property(e => e.LastError).HasMaxLength(1000);
    }
}
