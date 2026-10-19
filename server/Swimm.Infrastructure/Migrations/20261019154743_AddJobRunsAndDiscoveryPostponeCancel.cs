using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <summary>
    /// Часть В плана главной (docs/plans/competitions-home-plan.md): перенос/отмена у «Входящих»
    /// (Previous*, DateChangedAt, MissingSince — триггер версии на таблице уже есть) и журнал
    /// запусков фоновых расписаний Sys_JobRuns (Sys_, без гранта swimm_ro, НЕ в seed-tables —
    /// у каждой базы свой, поэтому триггера версии у него нет). 02-grants.sql не меняется:
    /// swimm_rw получает DML по ALTER DEFAULT PRIVILEGES из 01-roles.sql.
    /// </summary>
    public partial class AddJobRunsAndDiscoveryPostponeCancel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DateChangedAt",
                table: "Sys_DiscoveredCompetitions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "MissingSince",
                table: "Sys_DiscoveredCompetitions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PreviousDateEnd",
                table: "Sys_DiscoveredCompetitions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PreviousDateStart",
                table: "Sys_DiscoveredCompetitions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Sys_JobRuns",
                columns: table => new
                {
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    LastAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastSuccessAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sys_JobRuns", x => x.Name);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Sys_JobRuns");

            migrationBuilder.DropColumn(
                name: "DateChangedAt",
                table: "Sys_DiscoveredCompetitions");

            migrationBuilder.DropColumn(
                name: "MissingSince",
                table: "Sys_DiscoveredCompetitions");

            migrationBuilder.DropColumn(
                name: "PreviousDateEnd",
                table: "Sys_DiscoveredCompetitions");

            migrationBuilder.DropColumn(
                name: "PreviousDateStart",
                table: "Sys_DiscoveredCompetitions");
        }
    }
}
