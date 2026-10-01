using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOneOffAndCancelledTrainings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AsksRsvp",
                table: "Sys_TrainingSessions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "Sys_HubGroupTrainingCancellations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    HubGroupId = table.Column<int>(type: "integer", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Start = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    Reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sys_HubGroupTrainingCancellations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Sys_HubGroupTrainingCancellations_HubGroups_HubGroupId",
                        column: x => x.HubGroupId,
                        principalTable: "HubGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Sys_HubGroupTrainingCancellations_Sys_AppUsers_CreatedByUse~",
                        column: x => x.CreatedByUserId,
                        principalTable: "Sys_AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Sys_HubGroupTrainingCancellations_CreatedByUserId",
                table: "Sys_HubGroupTrainingCancellations",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Sys_HubGroupTrainingCancellations_HubGroupId_Date_Start",
                table: "Sys_HubGroupTrainingCancellations",
                columns: new[] { "HubGroupId", "Date", "Start" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Sys_HubGroupTrainingCancellations");

            migrationBuilder.DropColumn(
                name: "AsksRsvp",
                table: "Sys_TrainingSessions");
        }
    }
}
