using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDataCheckFindingSubjectAndFix : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FixEntityId",
                table: "Sys_DataCheckFindings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FixKind",
                table: "Sys_DataCheckFindings",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubjectName",
                table: "Sys_DataCheckFindings",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FixEntityId",
                table: "Sys_DataCheckFindings");

            migrationBuilder.DropColumn(
                name: "FixKind",
                table: "Sys_DataCheckFindings");

            migrationBuilder.DropColumn(
                name: "SubjectName",
                table: "Sys_DataCheckFindings");
        }
    }
}
