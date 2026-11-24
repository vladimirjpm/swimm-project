using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ResultLogligPlayer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LogligPlayerId",
                table: "Results",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Sys_LogligPlayers",
                columns: table => new
                {
                    LogligId = table.Column<int>(type: "integer", nullable: false),
                    FullName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    BirthYear = table.Column<int>(type: "integer", nullable: true),
                    SeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sys_LogligPlayers", x => x.LogligId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Results_LogligPlayer",
                table: "Results",
                columns: new[] { "CompetitionId", "SwimmerId", "LogligPlayerId" },
                filter: "\"LogligPlayerId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Sys_LogligPlayers");

            migrationBuilder.DropIndex(
                name: "IX_Results_LogligPlayer",
                table: "Results");

            migrationBuilder.DropColumn(
                name: "LogligPlayerId",
                table: "Results");
        }
    }
}
