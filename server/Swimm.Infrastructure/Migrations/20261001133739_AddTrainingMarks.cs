using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTrainingMarks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "MovedAt",
                table: "Sys_LanePlanSwimmers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MovedFromLaneNo",
                table: "Sys_LanePlanSwimmers",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Sys_HubGroupUserSeen",
                columns: table => new
                {
                    HubGroupId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    TrainingsSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LanesSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sys_HubGroupUserSeen", x => new { x.HubGroupId, x.UserId });
                    table.ForeignKey(
                        name: "FK_Sys_HubGroupUserSeen_HubGroups_HubGroupId",
                        column: x => x.HubGroupId,
                        principalTable: "HubGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Sys_HubGroupUserSeen_Sys_AppUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "Sys_AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Sys_HubGroupUserSeen_UserId",
                table: "Sys_HubGroupUserSeen",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Sys_HubGroupUserSeen");

            migrationBuilder.DropColumn(
                name: "MovedAt",
                table: "Sys_LanePlanSwimmers");

            migrationBuilder.DropColumn(
                name: "MovedFromLaneNo",
                table: "Sys_LanePlanSwimmers");
        }
    }
}
