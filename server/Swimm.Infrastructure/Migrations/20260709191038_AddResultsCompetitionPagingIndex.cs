using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddResultsCompetitionPagingIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Results_CompetitionId",
                table: "Results");

            migrationBuilder.CreateIndex(
                name: "IX_Results_CompetitionId_CompetitionDate_Position",
                table: "Results",
                columns: new[] { "CompetitionId", "CompetitionDate", "Position" },
                descending: new[] { false, true, false });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Results_CompetitionId_CompetitionDate_Position",
                table: "Results");

            migrationBuilder.CreateIndex(
                name: "IX_Results_CompetitionId",
                table: "Results",
                column: "CompetitionId");
        }
    }
}
