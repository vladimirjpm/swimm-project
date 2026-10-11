using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPointRuleClubsContextOptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Distances",
                table: "PointRulesClubs",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MinEntrants",
                table: "PointRulesClubs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OverallMultiplier",
                table: "PointRulesClubs",
                type: "integer",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "PointRulesClubs",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "Distances", "MinEntrants", "OverallMultiplier" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "PointRulesClubs",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "Distances", "MinEntrants", "OverallMultiplier" },
                values: new object[] { null, null, null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Distances",
                table: "PointRulesClubs");

            migrationBuilder.DropColumn(
                name: "MinEntrants",
                table: "PointRulesClubs");

            migrationBuilder.DropColumn(
                name: "OverallMultiplier",
                table: "PointRulesClubs");
        }
    }
}
