using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swimm.API.Migrations
{
    /// <inheritdoc />
    public partial class AddSysImportHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Sys_ImportHistory",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompetitionId = table.Column<int>(type: "int", nullable: false),
                    ImportFileName = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ImportDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Approved = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sys_ImportHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Sys_ImportHistory_Competitions_CompetitionId",
                        column: x => x.CompetitionId,
                        principalTable: "Competitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Sys_ImportHistory_CompetitionId",
                table: "Sys_ImportHistory",
                column: "CompetitionId");

            migrationBuilder.CreateIndex(
                name: "IX_Sys_ImportHistory_ImportDate",
                table: "Sys_ImportHistory",
                column: "ImportDate");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Sys_ImportHistory");
        }
    }
}
