using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TrainingSessionCreatedInApp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "Sys_TrainingSessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CreatedByUserId",
                table: "Sys_TrainingSessions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StartTime",
                table: "Sys_TrainingSessions",
                type: "character varying(5)",
                maxLength: 5,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Title",
                table: "Sys_TrainingSessions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sys_TrainingSessions_CreatedByUserId",
                table: "Sys_TrainingSessions",
                column: "CreatedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Sys_TrainingSessions_Sys_AppUsers_CreatedByUserId",
                table: "Sys_TrainingSessions",
                column: "CreatedByUserId",
                principalTable: "Sys_AppUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Sys_TrainingSessions_Sys_AppUsers_CreatedByUserId",
                table: "Sys_TrainingSessions");

            migrationBuilder.DropIndex(
                name: "IX_Sys_TrainingSessions_CreatedByUserId",
                table: "Sys_TrainingSessions");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "Sys_TrainingSessions");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "Sys_TrainingSessions");

            migrationBuilder.DropColumn(
                name: "StartTime",
                table: "Sys_TrainingSessions");

            migrationBuilder.DropColumn(
                name: "Title",
                table: "Sys_TrainingSessions");
        }
    }
}
