using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AllowSwimmerTagOnTrainingMedia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Р8 (01.10.2026): метка «кто на видео» у медиа тренировки — SwimmerId при TrainingId,
            // без Visibility = members. Якорь-заплыв (ResultId) — как был, только members.
            // ⚠ Down упадёт, если отмеченные ссылки тренировок уже есть: сперва снять метки.
            migrationBuilder.DropCheckConstraint(
                name: "CK_HubGroupMedia_AnchorMembersOnly",
                table: "Sys_HubGroupMedia");

            migrationBuilder.AddCheckConstraint(
                name: "CK_HubGroupMedia_AnchorMembersOnly",
                table: "Sys_HubGroupMedia",
                sql: "(\"ResultId\" IS NULL OR \"Visibility\" = 'members') AND (\"SwimmerId\" IS NULL OR \"Visibility\" = 'members' OR \"TrainingId\" IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_HubGroupMedia_AnchorMembersOnly",
                table: "Sys_HubGroupMedia");

            migrationBuilder.AddCheckConstraint(
                name: "CK_HubGroupMedia_AnchorMembersOnly",
                table: "Sys_HubGroupMedia",
                sql: "(\"SwimmerId\" IS NULL AND \"ResultId\" IS NULL) OR \"Visibility\" = 'members'");
        }
    }
}
