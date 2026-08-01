using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddClubCompetitionStandings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClubCompetitionStandings",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompetitionId = table.Column<int>(type: "integer", nullable: false),
                    ClubId = table.Column<int>(type: "integer", nullable: false),
                    Rank = table.Column<int>(type: "integer", nullable: false),
                    Points = table.Column<int>(type: "integer", nullable: false),
                    SwimmerCount = table.Column<int>(type: "integer", nullable: false),
                    ScoringSwims = table.Column<int>(type: "integer", nullable: false),
                    SwimCount = table.Column<int>(type: "integer", nullable: false),
                    Gold = table.Column<int>(type: "integer", nullable: false),
                    Silver = table.Column<int>(type: "integer", nullable: false),
                    Bronze = table.Column<int>(type: "integer", nullable: false),
                    ComputedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClubCompetitionStandings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClubCompetitionStandings_Clubs_ClubId",
                        column: x => x.ClubId,
                        principalTable: "Clubs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ClubCompetitionStandings_Competitions_CompetitionId",
                        column: x => x.CompetitionId,
                        principalTable: "Competitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClubCompetitionStandings_ClubId",
                table: "ClubCompetitionStandings",
                column: "ClubId");

            migrationBuilder.CreateIndex(
                name: "IX_ClubCompetitionStandings_CompetitionId_ClubId",
                table: "ClubCompetitionStandings",
                columns: new[] { "CompetitionId", "ClubId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClubCompetitionStandings_CompetitionId_Rank",
                table: "ClubCompetitionStandings",
                columns: new[] { "CompetitionId", "Rank" });

            // Публичная таблица (её читает страница клуба через read-путь) — нужен GRANT
            // read-only роли, иначе SwimmReadDbContext получит permission denied.
            migrationBuilder.Sql("GRANT SELECT ON \"ClubCompetitionStandings\" TO swimm_ro;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClubCompetitionStandings");
        }
    }
}
