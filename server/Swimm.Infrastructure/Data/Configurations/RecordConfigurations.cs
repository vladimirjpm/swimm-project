using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Swimm.Domain.Entities;

namespace Swimm.Infrastructure.Data.Configurations;

internal sealed class RecordConfiguration : IEntityTypeConfiguration<Record>
{
    public void Configure(EntityTypeBuilder<Record> entity)
    {
        entity.ToTable("Records");
        // 8 осей = уникальная позиция рекорда; RegionCode/AgeKey — NOT NULL (пустая строка),
        // иначе Postgres NULLS DISTINCT пропустил бы дубли.
        entity.HasIndex(e => new
        {
            e.RegionType, e.RegionCode, e.Category, e.AgeKey,
            e.Gender, e.PoolType, e.Style, e.Distance
        }).IsUnique();
        // Горячий путь клиента/кэша — выборка региона (± категории).
        entity.HasIndex(e => new { e.RegionType, e.RegionCode, e.Category });

        // TimeMs считает САМА БАЗА из Time (функция swim_time_ms, миграция
        // AddRecordTimeMs). Приложение её не пишет: рекорды заводят четыре разных места
        // плюс psql и восстановление дампа, и вычисляемая колонка — единственный способ
        // гарантировать, что она не разъедется с Time.
        entity.Property(e => e.TimeMs)
            .HasComputedColumnSql("swim_time_ms(\"Time\")", stored: true);
    }
}

// Реестр спорных записей справочника рекордов (docs/plans/records-quality-plan.md).
// Sys_-таблица: это НАША внутренняя кухня, а не данные федерации — публичной роли
// swimm_ro она не нужна. Ошибки источника мы не чиним, а помечаем здесь.
internal sealed class RecordIssueConfiguration : IEntityTypeConfiguration<RecordIssue>
{
    public void Configure(EntityTypeBuilder<RecordIssue> entity)
    {
        entity.ToTable("Sys_RecordIssues");
        // Ключ претензии — ось рекорда ПЛЮС оспариваемое время: одна и та же клетка
        // лестницы может попасть в реестр повторно, когда рекорд сменится.
        entity.HasIndex(e => new
        {
            e.RegionType, e.RegionCode, e.Category, e.AgeKey,
            e.Gender, e.PoolType, e.Style, e.Distance, e.FlaggedTime
        }).IsUnique();
        entity.HasIndex(e => e.Status);
    }
}

// Сверка рекордов с нашими протоколами. Одна строка на рекорд, каскад от Records:
// сверка бессмысленна без своего рекорда.
internal sealed class RecordVerificationConfiguration : IEntityTypeConfiguration<RecordVerification>
{
    public void Configure(EntityTypeBuilder<RecordVerification> entity)
    {
        entity.ToTable("Sys_RecordVerifications");
        entity.HasKey(e => e.RecordId);
        entity.HasOne(e => e.Record)
            .WithOne()
            .HasForeignKey<RecordVerification>(e => e.RecordId)
            .OnDelete(DeleteBehavior.Cascade);
        entity.HasIndex(e => e.Found);
        entity.Property(e => e.AgeAxisMatch).HasMaxLength(10);
        entity.HasIndex(e => e.AgeAxisMatch);
    }
}

// Журнал проверок источников рекордов (records-freshness-plan U1). Служебный: витрине
// отдаются только даты через сервис, в гранты swimm_ro таблица не входит.
internal sealed class RecordSourceCheckConfiguration : IEntityTypeConfiguration<RecordSourceCheck>
{
    public void Configure(EntityTypeBuilder<RecordSourceCheck> entity)
    {
        entity.ToTable("Sys_RecordSourceChecks");
        entity.Property(e => e.Source).HasMaxLength(40);
        entity.Property(e => e.Outcome).HasMaxLength(20);
        entity.Property(e => e.ContentHash).HasMaxLength(64);
        entity.Property(e => e.DiffId).HasMaxLength(32);
        entity.Property(e => e.Error).HasMaxLength(2000);
        // «Последняя проверка источника» — главный запрос журнала.
        entity.HasIndex(e => new { e.Source, e.CheckedAt });
        entity.HasIndex(e => e.DiffId);
    }
}

internal sealed class DebugOptionConfiguration : IEntityTypeConfiguration<DebugOption>
{
    public void Configure(EntityTypeBuilder<DebugOption> entity)
    {
        entity.ToTable("Sys_DebugOptions");
        entity.HasKey(e => e.Key);
        entity.Property(e => e.Key).HasMaxLength(60);
        entity.Property(e => e.Title).HasMaxLength(120);
        entity.Property(e => e.Description).HasMaxLength(400);
        entity.Property(e => e.UpdatedBy).HasMaxLength(200);
    }
}

internal sealed class NormativeStandardConfiguration : IEntityTypeConfiguration<NormativeStandard>
{
    public void Configure(EntityTypeBuilder<NormativeStandard> entity)
    {
        entity.ToTable("NormativeStandards");
        entity.HasIndex(e => new
        {
            e.Kind, e.Country, e.Gender, e.PoolType,
            e.Style, e.Distance, e.AgeKey, e.Level
        }).IsUnique();
    }
}
