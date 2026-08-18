using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ClubMergedInto : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MergedIntoId",
                table: "Clubs",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Clubs_MergedIntoId",
                table: "Clubs",
                column: "MergedIntoId");

            migrationBuilder.AddForeignKey(
                name: "FK_Clubs_Clubs_MergedIntoId",
                table: "Clubs",
                column: "MergedIntoId",
                principalTable: "Clubs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Clubs_Clubs_MergedIntoId",
                table: "Clubs");

            migrationBuilder.DropIndex(
                name: "IX_Clubs_MergedIntoId",
                table: "Clubs");

            migrationBuilder.DropColumn(
                name: "MergedIntoId",
                table: "Clubs");
        }
    }
}
