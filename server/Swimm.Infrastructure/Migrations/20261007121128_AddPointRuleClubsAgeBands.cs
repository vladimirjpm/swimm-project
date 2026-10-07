using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPointRuleClubsAgeBands : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AgeBands",
                table: "PointRulesClubs",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.UpdateData(
                table: "PointRulesClubs",
                keyColumn: "Id",
                keyValue: 1,
                column: "AgeBands",
                value: null);

            migrationBuilder.UpdateData(
                table: "PointRulesClubs",
                keyColumn: "Id",
                keyValue: 2,
                column: "AgeBands",
                value: null);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AgeBands",
                table: "PointRulesClubs");
        }
    }
}
