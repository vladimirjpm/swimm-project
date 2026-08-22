using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RecordVerificationAgeAxis : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AgeAxisMatch",
                table: "Sys_RecordVerifications",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sys_RecordVerifications_AgeAxisMatch",
                table: "Sys_RecordVerifications",
                column: "AgeAxisMatch");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Sys_RecordVerifications_AgeAxisMatch",
                table: "Sys_RecordVerifications");

            migrationBuilder.DropColumn(
                name: "AgeAxisMatch",
                table: "Sys_RecordVerifications");
        }
    }
}
