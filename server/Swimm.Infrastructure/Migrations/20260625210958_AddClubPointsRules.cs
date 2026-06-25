using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Swimm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddClubPointsRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClubPointsRules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    Description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Scope = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DefaultPoints = table.Column<int>(type: "integer", nullable: false),
                    MaxScoringPlace = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClubPointsRules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ClubPointsRuleEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RuleId = table.Column<int>(type: "integer", nullable: false),
                    Place = table.Column<int>(type: "integer", nullable: false),
                    Points = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClubPointsRuleEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClubPointsRuleEntries_ClubPointsRules_RuleId",
                        column: x => x.RuleId,
                        principalTable: "ClubPointsRules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "ClubPointsRules",
                columns: new[] { "Id", "DefaultPoints", "Description", "EffectiveFrom", "MaxScoringPlace", "Scope", "Version" },
                values: new object[,]
                {
                    { 1, 0, "Israeli swimming club points system starting Jan 2025", new DateOnly(2025, 1, 1), 24, "all", "2025.01" },
                    { 2, 0, "Masters club points system (places 1-12)", new DateOnly(2025, 1, 1), 12, "masters", "2025.01-masters" }
                });

            migrationBuilder.InsertData(
                table: "ClubPointsRuleEntries",
                columns: new[] { "Id", "Place", "Points", "RuleId" },
                values: new object[,]
                {
                    { 1, 1, 30, 1 },
                    { 2, 2, 28, 1 },
                    { 3, 3, 26, 1 },
                    { 4, 4, 24, 1 },
                    { 5, 5, 23, 1 },
                    { 6, 6, 22, 1 },
                    { 7, 7, 21, 1 },
                    { 8, 8, 20, 1 },
                    { 9, 9, 19, 1 },
                    { 10, 10, 18, 1 },
                    { 11, 11, 16, 1 },
                    { 12, 12, 15, 1 },
                    { 13, 13, 14, 1 },
                    { 14, 14, 13, 1 },
                    { 15, 15, 12, 1 },
                    { 16, 16, 11, 1 },
                    { 17, 17, 10, 1 },
                    { 18, 18, 9, 1 },
                    { 19, 19, 8, 1 },
                    { 20, 20, 7, 1 },
                    { 21, 21, 5, 1 },
                    { 22, 22, 3, 1 },
                    { 23, 23, 2, 1 },
                    { 24, 24, 1, 1 },
                    { 25, 1, 12, 2 },
                    { 26, 2, 11, 2 },
                    { 27, 3, 10, 2 },
                    { 28, 4, 9, 2 },
                    { 29, 5, 8, 2 },
                    { 30, 6, 7, 2 },
                    { 31, 7, 6, 2 },
                    { 32, 8, 5, 2 },
                    { 33, 9, 4, 2 },
                    { 34, 10, 3, 2 },
                    { 35, 11, 2, 2 },
                    { 36, 12, 1, 2 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClubPointsRuleEntries_RuleId_Place",
                table: "ClubPointsRuleEntries",
                columns: new[] { "RuleId", "Place" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClubPointsRules_Version",
                table: "ClubPointsRules",
                column: "Version",
                unique: true);

            // Открываем SELECT для роли swimm_ro (публичный read-путь).
            // Без этого гранта SwimmReadDbContext (swimm_ro) не сможет читать таблицы.
            migrationBuilder.Sql("GRANT SELECT ON \"ClubPointsRules\", \"ClubPointsRuleEntries\" TO swimm_ro;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClubPointsRuleEntries");

            migrationBuilder.DropTable(
                name: "ClubPointsRules");
        }
    }
}
