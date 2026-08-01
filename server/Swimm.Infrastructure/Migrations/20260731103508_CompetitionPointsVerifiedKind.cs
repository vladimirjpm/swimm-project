using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CompetitionPointsVerifiedKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ClubPointsVerifiedKind",
                table: "Competitions",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SwimmersPointsVerifiedKind",
                table: "Competitions",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            // Отметки, поставленные до появления двух итогов, означали «сверено с официальным
            // протоколом» — иначе строка осталась бы с датой, но без итога, и фильтр считал бы
            // её непроверенной.
            migrationBuilder.Sql(
                """
                UPDATE "Competitions" SET "ClubPointsVerifiedKind" = 'official'
                 WHERE "ClubPointsVerifiedAt" IS NOT NULL AND "ClubPointsVerifiedKind" IS NULL;
                UPDATE "Competitions" SET "SwimmersPointsVerifiedKind" = 'official'
                 WHERE "SwimmersPointsVerifiedAt" IS NOT NULL AND "SwimmersPointsVerifiedKind" IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ClubPointsVerifiedKind",
                table: "Competitions");

            migrationBuilder.DropColumn(
                name: "SwimmersPointsVerifiedKind",
                table: "Competitions");
        }
    }
}
