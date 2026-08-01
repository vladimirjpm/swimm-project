using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CompetitionPointsVerified : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ClubPointsVerifiedAt",
                table: "Competitions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClubPointsVerifiedBy",
                table: "Competitions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SwimmersPointsVerifiedAt",
                table: "Competitions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SwimmersPointsVerifiedBy",
                table: "Competitions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ClubPointsVerifiedAt",
                table: "Competitions");

            migrationBuilder.DropColumn(
                name: "ClubPointsVerifiedBy",
                table: "Competitions");

            migrationBuilder.DropColumn(
                name: "SwimmersPointsVerifiedAt",
                table: "Competitions");

            migrationBuilder.DropColumn(
                name: "SwimmersPointsVerifiedBy",
                table: "Competitions");
        }
    }
}
