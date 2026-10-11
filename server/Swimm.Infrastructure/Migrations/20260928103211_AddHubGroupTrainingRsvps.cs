using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddHubGroupTrainingRsvps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Sys_HubGroupTrainingRsvps",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    HubGroupId = table.Column<int>(type: "integer", nullable: false),
                    SessionDate = table.Column<DateOnly>(type: "date", nullable: false),
                    SessionStart = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Answer = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Note = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    SetByUserId = table.Column<int>(type: "integer", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sys_HubGroupTrainingRsvps", x => x.Id);
                    table.CheckConstraint("CK_HubGroupTrainingRsvps_Answer", "\"Answer\" IN ('yes', 'maybe', 'no')");
                    table.CheckConstraint("CK_HubGroupTrainingRsvps_Note", "\"Note\" IS NULL OR \"Note\" IN ('late', 'first-hour', 'leaving-early')");
                    table.ForeignKey(
                        name: "FK_Sys_HubGroupTrainingRsvps_HubGroups_HubGroupId",
                        column: x => x.HubGroupId,
                        principalTable: "HubGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Sys_HubGroupTrainingRsvps_Sys_AppUsers_SetByUserId",
                        column: x => x.SetByUserId,
                        principalTable: "Sys_AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Sys_HubGroupTrainingRsvps_Sys_AppUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "Sys_AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Sys_HubGroupTrainingRsvps_HubGroupId_SessionDate_SessionSta~",
                table: "Sys_HubGroupTrainingRsvps",
                columns: new[] { "HubGroupId", "SessionDate", "SessionStart", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sys_HubGroupTrainingRsvps_SetByUserId",
                table: "Sys_HubGroupTrainingRsvps",
                column: "SetByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Sys_HubGroupTrainingRsvps_UserId",
                table: "Sys_HubGroupTrainingRsvps",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Sys_HubGroupTrainingRsvps");
        }
    }
}
