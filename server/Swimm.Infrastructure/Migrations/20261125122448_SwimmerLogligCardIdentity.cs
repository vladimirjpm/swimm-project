using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SwimmerLogligCardIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CardBirthYear",
                table: "Sys_SwimmerLogligClubs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CardName",
                table: "Sys_SwimmerLogligClubs",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Gender",
                table: "Sys_SwimmerLogligClubs",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ResultsMatched",
                table: "Sys_SwimmerLogligClubs",
                type: "boolean",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CardBirthYear",
                table: "Sys_SwimmerLogligClubs");

            migrationBuilder.DropColumn(
                name: "CardName",
                table: "Sys_SwimmerLogligClubs");

            migrationBuilder.DropColumn(
                name: "Gender",
                table: "Sys_SwimmerLogligClubs");

            migrationBuilder.DropColumn(
                name: "ResultsMatched",
                table: "Sys_SwimmerLogligClubs");
        }
    }
}
