using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swimm.API.Migrations
{
    /// <inheritdoc />
    public partial class AddCountriesTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CountryId",
                table: "Swimmers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CountryId",
                table: "Results",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CountryId",
                table: "Clubs",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Countries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CountryCode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    CountryName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Countries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Swimmers_CountryId",
                table: "Swimmers",
                column: "CountryId");

            migrationBuilder.CreateIndex(
                name: "IX_Results_CountryId",
                table: "Results",
                column: "CountryId");

            migrationBuilder.CreateIndex(
                name: "IX_Clubs_CountryId",
                table: "Clubs",
                column: "CountryId");

            migrationBuilder.CreateIndex(
                name: "IX_Countries_CountryCode",
                table: "Countries",
                column: "CountryCode",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Clubs_Countries_CountryId",
                table: "Clubs",
                column: "CountryId",
                principalTable: "Countries",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Results_Countries_CountryId",
                table: "Results",
                column: "CountryId",
                principalTable: "Countries",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Swimmers_Countries_CountryId",
                table: "Swimmers",
                column: "CountryId",
                principalTable: "Countries",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Clubs_Countries_CountryId",
                table: "Clubs");

            migrationBuilder.DropForeignKey(
                name: "FK_Results_Countries_CountryId",
                table: "Results");

            migrationBuilder.DropForeignKey(
                name: "FK_Swimmers_Countries_CountryId",
                table: "Swimmers");

            migrationBuilder.DropTable(
                name: "Countries");

            migrationBuilder.DropIndex(
                name: "IX_Swimmers_CountryId",
                table: "Swimmers");

            migrationBuilder.DropIndex(
                name: "IX_Results_CountryId",
                table: "Results");

            migrationBuilder.DropIndex(
                name: "IX_Clubs_CountryId",
                table: "Clubs");

            migrationBuilder.DropColumn(
                name: "CountryId",
                table: "Swimmers");

            migrationBuilder.DropColumn(
                name: "CountryId",
                table: "Results");

            migrationBuilder.DropColumn(
                name: "CountryId",
                table: "Clubs");
        }
    }
}
