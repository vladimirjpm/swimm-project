using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CompetitionPartialResults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PartialResults",
                table: "Competitions",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PartialResultsAcceptedAt",
                table: "Competitions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PartialResultsAt",
                table: "Competitions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PartialResultsEn",
                table: "Competitions",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PartialResults",
                table: "Competitions");

            migrationBuilder.DropColumn(
                name: "PartialResultsAcceptedAt",
                table: "Competitions");

            migrationBuilder.DropColumn(
                name: "PartialResultsAt",
                table: "Competitions");

            migrationBuilder.DropColumn(
                name: "PartialResultsEn",
                table: "Competitions");
        }
    }
}
