using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLevelSubLevels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MaxPerLane",
                table: "Sys_LanePlans",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SubLevel",
                table: "Sys_HubGroupSwimmerLevels",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SubLevels",
                table: "Sys_HubGroupLevels",
                type: "integer",
                nullable: false,
                defaultValue: 1); // 1 = подуровней нет; 0 не прошёл бы CK_HubGroupLevels_SubLevels

            migrationBuilder.AddCheckConstraint(
                name: "CK_LanePlans_MaxPerLane",
                table: "Sys_LanePlans",
                sql: "\"MaxPerLane\" IS NULL OR \"MaxPerLane\" BETWEEN 1 AND 20");

            migrationBuilder.AddCheckConstraint(
                name: "CK_HubGroupSwimmerLevels_SubLevel",
                table: "Sys_HubGroupSwimmerLevels",
                sql: "\"SubLevel\" IS NULL OR \"SubLevel\" BETWEEN 1 AND 5");

            migrationBuilder.AddCheckConstraint(
                name: "CK_HubGroupLevels_SubLevels",
                table: "Sys_HubGroupLevels",
                sql: "\"SubLevels\" BETWEEN 1 AND 5");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_LanePlans_MaxPerLane",
                table: "Sys_LanePlans");

            migrationBuilder.DropCheckConstraint(
                name: "CK_HubGroupSwimmerLevels_SubLevel",
                table: "Sys_HubGroupSwimmerLevels");

            migrationBuilder.DropCheckConstraint(
                name: "CK_HubGroupLevels_SubLevels",
                table: "Sys_HubGroupLevels");

            migrationBuilder.DropColumn(
                name: "MaxPerLane",
                table: "Sys_LanePlans");

            migrationBuilder.DropColumn(
                name: "SubLevel",
                table: "Sys_HubGroupSwimmerLevels");

            migrationBuilder.DropColumn(
                name: "SubLevels",
                table: "Sys_HubGroupLevels");
        }
    }
}
