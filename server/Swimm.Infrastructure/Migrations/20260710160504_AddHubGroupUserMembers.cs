using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddHubGroupUserMembers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Sys_HubGroupUserMembers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    HubGroupId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    AddedByUserId = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    JoinedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sys_HubGroupUserMembers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Sys_HubGroupUserMembers_HubGroups_HubGroupId",
                        column: x => x.HubGroupId,
                        principalTable: "HubGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Sys_HubGroupUserMembers_Sys_AppUsers_AddedByUserId",
                        column: x => x.AddedByUserId,
                        principalTable: "Sys_AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Sys_HubGroupUserMembers_Sys_AppUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "Sys_AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Sys_HubGroupUserMembers_AddedByUserId",
                table: "Sys_HubGroupUserMembers",
                column: "AddedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Sys_HubGroupUserMembers_HubGroupId_UserId",
                table: "Sys_HubGroupUserMembers",
                columns: new[] { "HubGroupId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sys_HubGroupUserMembers_UserId",
                table: "Sys_HubGroupUserMembers",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Sys_HubGroupUserMembers");
        }
    }
}
