using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRecordSourceChecks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Sys_RecordSourceChecks",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Source = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CheckedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Outcome = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AddedCount = table.Column<int>(type: "integer", nullable: true),
                    ChangedCount = table.Column<int>(type: "integer", nullable: true),
                    MissingCount = table.Column<int>(type: "integer", nullable: true),
                    DiffId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    AppliedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sys_RecordSourceChecks", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Sys_RecordSourceChecks_DiffId",
                table: "Sys_RecordSourceChecks",
                column: "DiffId");

            migrationBuilder.CreateIndex(
                name: "IX_Sys_RecordSourceChecks_Source_CheckedAt",
                table: "Sys_RecordSourceChecks",
                columns: new[] { "Source", "CheckedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Sys_RecordSourceChecks");
        }
    }
}
