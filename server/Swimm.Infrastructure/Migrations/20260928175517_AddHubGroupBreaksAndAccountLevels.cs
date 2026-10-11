using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddHubGroupBreaksAndAccountLevels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Sys_HubGroupAccountLevels",
                columns: table => new
                {
                    HubGroupId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    LevelId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sys_HubGroupAccountLevels", x => new { x.HubGroupId, x.UserId });
                    table.ForeignKey(
                        name: "FK_Sys_HubGroupAccountLevels_HubGroups_HubGroupId",
                        column: x => x.HubGroupId,
                        principalTable: "HubGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Sys_HubGroupAccountLevels_Sys_AppUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "Sys_AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Sys_HubGroupAccountLevels_Sys_HubGroupLevels_HubGroupId_Lev~",
                        columns: x => new { x.HubGroupId, x.LevelId },
                        principalTable: "Sys_HubGroupLevels",
                        principalColumns: new[] { "HubGroupId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Sys_HubGroupBreaks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    HubGroupId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: true),
                    SwimmerId = table.Column<int>(type: "integer", nullable: true),
                    Since = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Until = table.Column<DateOnly>(type: "date", nullable: true),
                    EndedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EndedByRsvp = table.Column<bool>(type: "boolean", nullable: false),
                    SetByUserId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sys_HubGroupBreaks", x => x.Id);
                    table.CheckConstraint("CK_HubGroupBreaks_Subject", "(\"UserId\" IS NULL) <> (\"SwimmerId\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_Sys_HubGroupBreaks_HubGroups_HubGroupId",
                        column: x => x.HubGroupId,
                        principalTable: "HubGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Sys_HubGroupBreaks_Swimmers_SwimmerId",
                        column: x => x.SwimmerId,
                        principalTable: "Swimmers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Sys_HubGroupBreaks_Sys_AppUsers_SetByUserId",
                        column: x => x.SetByUserId,
                        principalTable: "Sys_AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Sys_HubGroupBreaks_Sys_AppUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "Sys_AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Sys_HubGroupAccountLevels_HubGroupId_LevelId",
                table: "Sys_HubGroupAccountLevels",
                columns: new[] { "HubGroupId", "LevelId" });

            migrationBuilder.CreateIndex(
                name: "IX_Sys_HubGroupAccountLevels_UserId",
                table: "Sys_HubGroupAccountLevels",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Sys_HubGroupBreaks_HubGroupId_SwimmerId",
                table: "Sys_HubGroupBreaks",
                columns: new[] { "HubGroupId", "SwimmerId" },
                unique: true,
                filter: "\"EndedAt\" IS NULL AND \"SwimmerId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Sys_HubGroupBreaks_HubGroupId_UserId",
                table: "Sys_HubGroupBreaks",
                columns: new[] { "HubGroupId", "UserId" },
                unique: true,
                filter: "\"EndedAt\" IS NULL AND \"UserId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Sys_HubGroupBreaks_SetByUserId",
                table: "Sys_HubGroupBreaks",
                column: "SetByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Sys_HubGroupBreaks_SwimmerId",
                table: "Sys_HubGroupBreaks",
                column: "SwimmerId");

            migrationBuilder.CreateIndex(
                name: "IX_Sys_HubGroupBreaks_UserId",
                table: "Sys_HubGroupBreaks",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Sys_HubGroupAccountLevels");

            migrationBuilder.DropTable(
                name: "Sys_HubGroupBreaks");
        }
    }
}
