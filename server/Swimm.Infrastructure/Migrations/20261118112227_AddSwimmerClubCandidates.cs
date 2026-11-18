using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSwimmerClubCandidates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Sys_SwimmerClubChanges_Source",
                table: "Sys_SwimmerClubChanges");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Sys_JobRunLog_Trigger",
                table: "Sys_JobRunLog");

            migrationBuilder.CreateTable(
                name: "Sys_SwimmerClubCandidates",
                columns: table => new
                {
                    SwimmerId = table.Column<int>(type: "integer", nullable: false),
                    FromClubId = table.Column<int>(type: "integer", nullable: true),
                    ToClubId = table.Column<int>(type: "integer", nullable: false),
                    LastSwimDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Overlap = table.Column<bool>(type: "boolean", nullable: false),
                    Source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Verdict = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    LogligId = table.Column<int>(type: "integer", nullable: true),
                    LogligClubName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CheckedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sys_SwimmerClubCandidates", x => x.SwimmerId);
                    table.CheckConstraint("CK_Sys_SwimmerClubCandidates_Source", "\"Source\" IN ('import', 'cli')");
                    table.CheckConstraint("CK_Sys_SwimmerClubCandidates_Verdict", "\"Verdict\" IS NULL OR \"Verdict\" IN ('confirmed', 'old', 'archive', 'other', 'no-loglig-id', 'unavailable')");
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Sys_SwimmerClubChanges_Source",
                table: "Sys_SwimmerClubChanges",
                sql: "\"Source\" IN ('import', 'cli', 'admin', 'loglig')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Sys_JobRunLog_Trigger",
                table: "Sys_JobRunLog",
                sql: "\"Trigger\" IN ('schedule', 'manual', 'cli', 'import')");

            migrationBuilder.CreateIndex(
                name: "IX_Sys_SwimmerClubCandidates_Verdict",
                table: "Sys_SwimmerClubCandidates",
                column: "Verdict");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Sys_SwimmerClubCandidates");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Sys_SwimmerClubChanges_Source",
                table: "Sys_SwimmerClubChanges");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Sys_JobRunLog_Trigger",
                table: "Sys_JobRunLog");

            // Новые значения старый CHECK не пустит: смены по loglig помечаем «cli» (автоматика),
            // проходы «после импорта» из истории джобов удаляем — иначе откат упадёт.
            migrationBuilder.Sql("UPDATE \"Sys_SwimmerClubChanges\" SET \"Source\" = 'cli' WHERE \"Source\" = 'loglig';");
            migrationBuilder.Sql("DELETE FROM \"Sys_JobRunLog\" WHERE \"Trigger\" = 'import';");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Sys_SwimmerClubChanges_Source",
                table: "Sys_SwimmerClubChanges",
                sql: "\"Source\" IN ('import', 'cli', 'admin')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Sys_JobRunLog_Trigger",
                table: "Sys_JobRunLog",
                sql: "\"Trigger\" IN ('schedule', 'manual', 'cli')");
        }
    }
}
