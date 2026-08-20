using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCompetitionNoteScaleDiffCaption : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ScaleDiffCaption",
                table: "CompetitionNotes",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ScaleDiffCaption",
                table: "CompetitionNotes");
        }
    }
}
