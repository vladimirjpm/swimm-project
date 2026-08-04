using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDiscoveryEmptySource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "EmptySourceAt",
                table: "Sys_DiscoveredCompetitions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmptySourceBy",
                table: "Sys_DiscoveredCompetitions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EmptySourceAt",
                table: "Sys_DiscoveredCompetitions");

            migrationBuilder.DropColumn(
                name: "EmptySourceBy",
                table: "Sys_DiscoveredCompetitions");
        }
    }
}
