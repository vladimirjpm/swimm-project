using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ResultsCombinedPlace : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BestTimeMs",
                table: "Results",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CombinedPlace",
                table: "Results",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsBestResult",
                table: "Results",
                type: "boolean",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BestTimeMs",
                table: "Results");

            migrationBuilder.DropColumn(
                name: "CombinedPlace",
                table: "Results");

            migrationBuilder.DropColumn(
                name: "IsBestResult",
                table: "Results");
        }
    }
}
