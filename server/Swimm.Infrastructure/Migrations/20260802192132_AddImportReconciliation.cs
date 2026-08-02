using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddImportReconciliation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Sys_ImportReconciliation",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompetitionId = table.Column<int>(type: "integer", nullable: false),
                    ImportedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ImportFileName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    EventKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ExpectedRows = table.Column<int>(type: "integer", nullable: false),
                    ActualRows = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sys_ImportReconciliation", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Sys_ImportReconciliation_Competitions_CompetitionId",
                        column: x => x.CompetitionId,
                        principalTable: "Competitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Sys_ImportReconciliation_CompetitionId",
                table: "Sys_ImportReconciliation",
                column: "CompetitionId");

            migrationBuilder.CreateIndex(
                name: "IX_Sys_ImportReconciliation_ImportedAt",
                table: "Sys_ImportReconciliation",
                column: "ImportedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Sys_ImportReconciliation_Mismatch",
                table: "Sys_ImportReconciliation",
                column: "Status",
                filter: "\"Status\" = 'mismatch'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Sys_ImportReconciliation");
        }
    }
}
