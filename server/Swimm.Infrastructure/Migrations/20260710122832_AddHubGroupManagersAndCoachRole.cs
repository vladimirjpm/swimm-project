using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddHubGroupManagersAndCoachRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Sys_HubGroupManagers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    HubGroupId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    GrantedByUserId = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sys_HubGroupManagers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Sys_HubGroupManagers_HubGroups_HubGroupId",
                        column: x => x.HubGroupId,
                        principalTable: "HubGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Sys_HubGroupManagers_Sys_AppUsers_GrantedByUserId",
                        column: x => x.GrantedByUserId,
                        principalTable: "Sys_AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Sys_HubGroupManagers_Sys_AppUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "Sys_AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "Sys_AppRoles",
                columns: new[] { "Id", "Name" },
                values: new object[] { 3, "Coach" });

            migrationBuilder.CreateIndex(
                name: "IX_Sys_HubGroupManagers_GrantedByUserId",
                table: "Sys_HubGroupManagers",
                column: "GrantedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Sys_HubGroupManagers_HubGroupId_UserId",
                table: "Sys_HubGroupManagers",
                columns: new[] { "HubGroupId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sys_HubGroupManagers_UserId",
                table: "Sys_HubGroupManagers",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Sys_HubGroupManagers");

            migrationBuilder.DeleteData(
                table: "Sys_AppRoles",
                keyColumn: "Id",
                keyValue: 3);
        }
    }
}
