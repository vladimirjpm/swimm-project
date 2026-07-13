using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddHubGroupCountry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CountryId",
                table: "HubGroups",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_HubGroups_CountryId",
                table: "HubGroups",
                column: "CountryId");

            migrationBuilder.AddForeignKey(
                name: "FK_HubGroups_Countries_CountryId",
                table: "HubGroups",
                column: "CountryId",
                principalTable: "Countries",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_HubGroups_Countries_CountryId",
                table: "HubGroups");

            migrationBuilder.DropIndex(
                name: "IX_HubGroups_CountryId",
                table: "HubGroups");

            migrationBuilder.DropColumn(
                name: "CountryId",
                table: "HubGroups");
        }
    }
}
