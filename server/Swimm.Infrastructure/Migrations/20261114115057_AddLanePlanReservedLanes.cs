using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLanePlanReservedLanes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ReservedLanes",
                table: "Sys_LanePlans",
                type: "integer",
                nullable: false,
                // Существующим планам — как новым: одна дорожка про запас (решение Влада 14.11.2026).
                defaultValue: 1);

            migrationBuilder.AddCheckConstraint(
                name: "CK_LanePlans_ReservedLanes",
                table: "Sys_LanePlans",
                sql: "\"ReservedLanes\" BETWEEN 0 AND 11");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_LanePlans_ReservedLanes",
                table: "Sys_LanePlans");

            migrationBuilder.DropColumn(
                name: "ReservedLanes",
                table: "Sys_LanePlans");
        }
    }
}
