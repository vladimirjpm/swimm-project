using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CompetitionCountryToFk : Migration
    {
        // Competitions.Country: строковый alpha-3 код → FK на Countries (как у Swimmer/Club/
        // HubGroup). Порядок EF по умолчанию (Drop до Add) терял бы данные — переписано вручную:
        // сначала добавляем CountryId и бэкфиллим из строки, только потом дропаем строку.
        // Коды предполагаются нормализованными (миграция MergeCountryIlIntoIsr).

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CountryId",
                table: "Competitions",
                type: "integer",
                nullable: true);

            // Бэкфилл: строковый код → Id справочника. Непустые коды без строки в Countries
            // (не должно быть после нормализации) останутся с CountryId = null.
            migrationBuilder.Sql(@"
UPDATE ""Competitions"" AS comp
SET ""CountryId"" = c.""Id""
FROM ""Countries"" c
WHERE c.""CountryCode"" = comp.""Country"" AND comp.""Country"" <> '';
");

            migrationBuilder.CreateIndex(
                name: "IX_Competitions_CountryId",
                table: "Competitions",
                column: "CountryId");

            migrationBuilder.AddForeignKey(
                name: "FK_Competitions_Countries_CountryId",
                table: "Competitions",
                column: "CountryId",
                principalTable: "Countries",
                principalColumn: "Id");

            migrationBuilder.DropColumn(
                name: "Country",
                table: "Competitions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Country",
                table: "Competitions",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "");

            // Обратный бэкфилл: FK → строковый код (null CountryId → пустая строка).
            migrationBuilder.Sql(@"
UPDATE ""Competitions"" AS comp
SET ""Country"" = COALESCE(
    (SELECT c.""CountryCode"" FROM ""Countries"" c WHERE c.""Id"" = comp.""CountryId""), '');
");

            migrationBuilder.DropForeignKey(
                name: "FK_Competitions_Countries_CountryId",
                table: "Competitions");

            migrationBuilder.DropIndex(
                name: "IX_Competitions_CountryId",
                table: "Competitions");

            migrationBuilder.DropColumn(
                name: "CountryId",
                table: "Competitions");
        }
    }
}
