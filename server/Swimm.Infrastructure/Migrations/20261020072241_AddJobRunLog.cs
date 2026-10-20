using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <summary>
    /// История прогонов фоновых задач с итогом — Sys_JobRunLog (страница /Admin/Jobs). Как и
    /// Sys_JobRuns: Sys_, без гранта swimm_ro, НЕ в seed-tables (у каждой базы свой журнал, поэтому
    /// триггера версии нет). 02-grants.sql не меняется: swimm_rw получает DML по ALTER DEFAULT
    /// PRIVILEGES из 01-roles.sql.
    /// </summary>
    public partial class AddJobRunLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Sys_JobRunLog",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Trigger = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FinishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Success = table.Column<bool>(type: "boolean", nullable: false),
                    Summary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sys_JobRunLog", x => x.Id);
                    table.CheckConstraint("CK_Sys_JobRunLog_Trigger", "\"Trigger\" IN ('schedule', 'manual', 'cli')");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Sys_JobRunLog_Name_StartedAt",
                table: "Sys_JobRunLog",
                columns: new[] { "Name", "StartedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Sys_JobRunLog");
        }
    }
}
