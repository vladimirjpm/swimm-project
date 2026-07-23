using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSwimmerLogligId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LogligId",
                table: "Swimmers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LogligIdSource",
                table: "Swimmers",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LogligIdStatus",
                table: "Swimmers",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LogligIdSuggestedAt",
                table: "Swimmers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LogligIdSuggestedByUserId",
                table: "Swimmers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LogligIdVerifiedAt",
                table: "Swimmers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Swimmers_LogligId",
                table: "Swimmers",
                column: "LogligId",
                unique: true,
                filter: "\"LogligId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Swimmers_LogligId",
                table: "Swimmers");

            migrationBuilder.DropColumn(
                name: "LogligId",
                table: "Swimmers");

            migrationBuilder.DropColumn(
                name: "LogligIdSource",
                table: "Swimmers");

            migrationBuilder.DropColumn(
                name: "LogligIdStatus",
                table: "Swimmers");

            migrationBuilder.DropColumn(
                name: "LogligIdSuggestedAt",
                table: "Swimmers");

            migrationBuilder.DropColumn(
                name: "LogligIdSuggestedByUserId",
                table: "Swimmers");

            migrationBuilder.DropColumn(
                name: "LogligIdVerifiedAt",
                table: "Swimmers");
        }
    }
}
