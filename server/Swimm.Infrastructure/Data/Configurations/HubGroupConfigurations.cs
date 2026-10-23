using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Swimm.Domain.Entities;

namespace Swimm.Infrastructure.Data.Configurations;

internal sealed class HubGroupConfiguration : IEntityTypeConfiguration<HubGroup>
{
    public void Configure(EntityTypeBuilder<HubGroup> entity)
    {
        entity.ToTable("HubGroups");

        entity.HasOne(e => e.Club)
            .WithMany()
            .HasForeignKey(e => e.ClubId)
            .OnDelete(DeleteBehavior.SetNull);

        // FK на Sys_AppUsers допустим (FK на уровне БД не требует SELECT-права у swimm_ro);
        // навигацию Owner не Include-ить нигде за пределами админки.
        // Restrict намеренно: удаление аккаунта обязано решить судьбу его групп явно
        // (AccountDeletionRules), а не потерять их молча.
        entity.HasOne(e => e.Owner)
            .WithMany()
            .HasForeignKey(e => e.OwnerUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // Официальна СВЯЗЬ с клубом, не состав: одна официальная группа на клуб,
        // и она всегда привязана к клубу (фаза 8.7).
        entity.HasIndex(e => e.ClubId)
            .IsUnique()
            .HasFilter(@"""IsOfficial""")
            .HasDatabaseName("IX_HubGroups_ClubId_Official");

        entity.HasCheckConstraint(
            "CK_HubGroups_OfficialRequiresClub",
            @"NOT ""IsOfficial"" OR ""ClubId"" IS NOT NULL");

        // Тестовая группа не бывает официальной: иначе она стала бы «лицом клуба» и убрала
        // из каталога настоящие группы, подписанные на этот клуб (П4).
        entity.HasCheckConstraint(
            "CK_HubGroups_TestNotOfficial",
            @"NOT (""IsTest"" AND ""IsOfficial"")");

        entity.HasCheckConstraint(
            "CK_HubGroups_JoinPolicy",
            @"""JoinPolicy"" IN ('open', 'approval')");
    }
}

internal sealed class HubGroupMemberConfiguration : IEntityTypeConfiguration<HubGroupMember>
{
    public void Configure(EntityTypeBuilder<HubGroupMember> entity)
    {
        entity.ToTable("HubGroupMembers");

        entity.HasOne(e => e.HubGroup)
            .WithMany(g => g.Members)
            .HasForeignKey(e => e.HubGroupId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne(e => e.Swimmer)
            .WithMany()
            .HasForeignKey(e => e.SwimmerId)
            .OnDelete(DeleteBehavior.Cascade);

        // Дефолт на уровне БД — им же миграция размечает существующий состав: всё, что было
        // до подписки на клуб, добавлено руками. IsExcluded дефолта в модели не держит: для
        // bool EF не отличил бы «явно false» от «не задано», а существующим строкам false
        // и так проставляет AddColumn.
        entity.Property(e => e.Source).HasDefaultValue(HubGroupMemberSource.Manual);

        entity.HasCheckConstraint(
            "CK_HubGroupMembers_Source",
            @"""Source"" IN ('manual', 'club', 'private')");

        // Скрыть (IsExcluded, «Inactive») можно строку любого вида: клубного, пловца группы
        // (Р71) и — с 30.09.2026 (Р74) — ручного: «ушёл из группы», но вернуть одной кнопкой
        // Restore. Ограничение CK_HubGroupMembers_ExcludedOnlyClub снято. Скрытую строку все
        // читатели состава пропускают (!IsExcluded); пересборка по клубу ручных не трогает.
    }
}

// Подписка группы на клуб (docs/plans/hubgroup-club-subscription-plan.md) — бизнес-
// таблица, ПУБЛИЧНАЯ: её читает анонимный путь (каталог, строка «Follows club») →
// грант swimm_ro в миграции и в server/db/02-grants.sql.
internal sealed class HubGroupClubSubscriptionConfiguration : IEntityTypeConfiguration<HubGroupClubSubscription>
{
    public void Configure(EntityTypeBuilder<HubGroupClubSubscription> entity)
    {
        entity.ToTable("HubGroupClubSubscriptions");

        // Одна подписка на группу (решение 10.09.2026). Разрешить несколько = снять
        // уникальность здесь, модель и сервис менять не придётся.
        entity.HasIndex(e => e.HubGroupId).IsUnique();

        // Пересборка после импорта ищет подписки по клубам импорта.
        entity.HasIndex(e => e.ClubId);

        entity.HasOne(e => e.HubGroup)
            .WithMany(g => g.ClubSubscriptions)
            .HasForeignKey(e => e.HubGroupId)
            .OnDelete(DeleteBehavior.Cascade);

        // Клубы склеиваются мягко (MergedIntoId), строку не удаляют; удаляется только
        // пустой клуб (--delete-empty-clubs), а у пустого и подписка пуста.
        entity.HasOne(e => e.Club)
            .WithMany()
            .HasForeignKey(e => e.ClubId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

// Админы группы (право правки) — таблица прав, БЕЗ grant swimm_ro.
internal sealed class HubGroupAdminConfiguration : IEntityTypeConfiguration<HubGroupAdmin>
{
    public void Configure(EntityTypeBuilder<HubGroupAdmin> entity)
    {
        entity.ToTable("Sys_HubGroupAdmins");
        entity.HasIndex(e => new { e.HubGroupId, e.UserId }).IsUnique();

        entity.HasOne(e => e.HubGroup)
            .WithMany(g => g.Admins)
            .HasForeignKey(e => e.HubGroupId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // SetNull: назначивший удалил аккаунт — админство остаётся, автор пропадает.
        entity.HasOne(e => e.GrantedBy)
            .WithMany()
            .HasForeignKey(e => e.GrantedByUserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

// Участники-аккаунты (приватный состав из юзеров) — таблица личных данных, БЕЗ grant swimm_ro.
internal sealed class HubGroupUserMemberConfiguration : IEntityTypeConfiguration<HubGroupUserMember>
{
    public void Configure(EntityTypeBuilder<HubGroupUserMember> entity)
    {
        entity.ToTable("Sys_HubGroupUserMembers");
        entity.HasIndex(e => new { e.HubGroupId, e.UserId }).IsUnique();

        entity.HasOne(e => e.HubGroup)
            .WithMany(g => g.UserMembers)
            .HasForeignKey(e => e.HubGroupId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // SetNull: добавивший удалил аккаунт — участие остаётся. ⚠ После этого AddedByUserId == null
        // читается как «вступил сам» (SelfJoined в админке) — принято, account-deletion-plan.md У0.
        entity.HasOne(e => e.AddedBy)
            .WithMany()
            .HasForeignKey(e => e.AddedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        entity.HasOne(e => e.Swimmer)
            .WithMany()
            .HasForeignKey(e => e.SwimmerId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

// Заявки на официальный статус группы (право/личные данные) — Sys_-таблица, БЕЗ grant swimm_ro.
internal sealed class HubGroupClubRequestConfiguration : IEntityTypeConfiguration<HubGroupClubRequest>
{
    public void Configure(EntityTypeBuilder<HubGroupClubRequest> entity)
    {
        entity.ToTable("Sys_HubGroupClubRequests");

        // Одна pending-заявка на группу одновременно.
        entity.HasIndex(e => e.HubGroupId)
            .IsUnique()
            .HasFilter(@"""Status"" = 'pending'")
            .HasDatabaseName("IX_Sys_HubGroupClubRequests_HubGroupId_Pending");

        entity.HasOne(e => e.HubGroup)
            .WithMany()
            .HasForeignKey(e => e.HubGroupId)
            .OnDelete(DeleteBehavior.Cascade);

        // Restrict намеренно: заявитель — владелец группы на момент подачи; передачи групп нет,
        // поэтому при удалении аккаунта его заявки уходят каскадом вместе с его группой.
        entity.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne(e => e.Club)
            .WithMany()
            .HasForeignKey(e => e.ClubId)
            .OnDelete(DeleteBehavior.Restrict);

        // SetNull: решивший (админ сайта) удалил аккаунт — решение остаётся без автора.
        entity.HasOne(e => e.DecidedByUser)
            .WithMany()
            .HasForeignKey(e => e.DecidedByUserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

// Медиа группы (только ссылки) — TrainingId null = публичная галерея, задан = медиа
// тренировки (та же приватность, что у Sys_TrainingSessions). Sys_-таблица БЕЗ grant
// swimm_ro: публичная галерея читается через SwimmDbContext, не read-реплику.
internal sealed class HubGroupMediaConfiguration : IEntityTypeConfiguration<HubGroupMedia>
{
    public void Configure(EntityTypeBuilder<HubGroupMedia> entity)
    {
        entity.ToTable("Sys_HubGroupMedia");
        entity.HasIndex(e => e.HubGroupId);
        entity.HasIndex(e => e.TrainingId);

        entity.HasOne(e => e.HubGroup)
            .WithMany()
            .HasForeignKey(e => e.HubGroupId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne(e => e.Training)
            .WithMany()
            .HasForeignKey(e => e.TrainingId)
            .OnDelete(DeleteBehavior.Cascade);

        // Автор — и участник (видео к тренировке, Е2): его аккаунт не должен держать ссылку
        // группы RESTRICT-ом, удалили аккаунт — медиа остаётся без автора.
        entity.HasOne(e => e.CreatedByUser)
            .WithMany()
            .HasForeignKey(e => e.CreatedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        // Якоря 2B′: удаление пловца/заплыва уносит и привязанные разборы (cascade) —
        // медиа-ссылка без своего якоря бессмысленна.
        entity.HasOne(e => e.Swimmer)
            .WithMany()
            .HasForeignKey(e => e.SwimmerId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne(e => e.Result)
            .WithMany()
            .HasForeignKey(e => e.ResultId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasCheckConstraint(
            "CK_HubGroupMedia_MediaType",
            @"""MediaType"" IN ('image', 'video', 'album')");

        entity.HasCheckConstraint(
            "CK_HubGroupMedia_SourceType",
            @"""SourceType"" IN ('youtube', 'vimeo', 'album', 'other')");

        entity.HasCheckConstraint(
            "CK_HubGroupMedia_AlbumInvariant",
            @"(""MediaType"" = 'album') = (""SourceType"" = 'album')");

        entity.HasCheckConstraint(
            "CK_HubGroupMedia_Visibility",
            @"""Visibility"" IN ('public', 'members')");

        // Якорь на персону — только members-слой: публично вешать медиа на пловца нельзя.
        // Исключение (Р8, 01.10.2026): метка «кто на видео» у медиа ТРЕНИРОВКИ (TrainingId задан) —
        // её аудитория — аудитория тренировки, Visibility там не используется. Якорь-заплыв
        // (ResultId) — по-прежнему только members.
        entity.HasCheckConstraint(
            "CK_HubGroupMedia_AnchorMembersOnly",
            @"(""ResultId"" IS NULL OR ""Visibility"" = 'members') AND (""SwimmerId"" IS NULL OR ""Visibility"" = 'members' OR ""TrainingId"" IS NOT NULL)");
    }
}
