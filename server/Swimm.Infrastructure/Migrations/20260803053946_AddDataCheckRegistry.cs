using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDataCheckRegistry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Sys_DataCheckFindings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CheckId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Severity = table.Column<int>(type: "integer", nullable: false),
                    EntityType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    EntityId = table.Column<int>(type: "integer", nullable: true),
                    Message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Details = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Link = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    FirstSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Resolution = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    ResolvedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sys_DataCheckFindings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Sys_DataCheckRuns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FinishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Trigger = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ErrorCount = table.Column<int>(type: "integer", nullable: false),
                    WarningCount = table.Column<int>(type: "integer", nullable: false),
                    InfoCount = table.Column<int>(type: "integer", nullable: false),
                    FixedCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sys_DataCheckRuns", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Sys_DataCheckFindings_CheckId_EntityType_EntityId",
                table: "Sys_DataCheckFindings",
                columns: new[] { "CheckId", "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_Sys_DataCheckFindings_Open",
                table: "Sys_DataCheckFindings",
                column: "Resolution",
                filter: "\"Resolution\" IS NULL OR \"Resolution\" = 'accepted'");

            migrationBuilder.CreateIndex(
                name: "IX_Sys_DataCheckRuns_StartedAt",
                table: "Sys_DataCheckRuns",
                column: "StartedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Sys_DataCheckFindings");

            migrationBuilder.DropTable(
                name: "Sys_DataCheckRuns");
        }
    }
}
