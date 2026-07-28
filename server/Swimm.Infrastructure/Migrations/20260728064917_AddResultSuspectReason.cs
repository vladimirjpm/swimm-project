using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddResultSuspectReason : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "SuspectIsManual",
                table: "Results",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "SuspectNote",
                table: "Results",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SuspectReason",
                table: "Results",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Results_SuspectReason",
                table: "Results",
                column: "SuspectReason",
                filter: "\"SuspectReason\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Results_SuspectReason",
                table: "Results");

            migrationBuilder.DropColumn(
                name: "SuspectIsManual",
                table: "Results");

            migrationBuilder.DropColumn(
                name: "SuspectNote",
                table: "Results");

            migrationBuilder.DropColumn(
                name: "SuspectReason",
                table: "Results");
        }
    }
}
