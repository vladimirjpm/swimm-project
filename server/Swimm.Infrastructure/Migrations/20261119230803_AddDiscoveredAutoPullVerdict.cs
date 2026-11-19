using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDiscoveredAutoPullVerdict : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AutoPullBlockedReason",
                table: "Sys_DiscoveredCompetitions",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AutoPullCheckedAt",
                table: "Sys_DiscoveredCompetitions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AutoPullPoolType",
                table: "Sys_DiscoveredCompetitions",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AutoPullVerdict",
                table: "Sys_DiscoveredCompetitions",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Sys_DiscoveredCompetitions_AutoPullVerdict",
                table: "Sys_DiscoveredCompetitions",
                sql: "\"AutoPullVerdict\" IS NULL OR \"AutoPullVerdict\" IN ('would-take', 'blocked', 'not-finished', 'failed', 'empty')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Sys_DiscoveredCompetitions_AutoPullVerdict",
                table: "Sys_DiscoveredCompetitions");

            migrationBuilder.DropColumn(
                name: "AutoPullBlockedReason",
                table: "Sys_DiscoveredCompetitions");

            migrationBuilder.DropColumn(
                name: "AutoPullCheckedAt",
                table: "Sys_DiscoveredCompetitions");

            migrationBuilder.DropColumn(
                name: "AutoPullPoolType",
                table: "Sys_DiscoveredCompetitions");

            migrationBuilder.DropColumn(
                name: "AutoPullVerdict",
                table: "Sys_DiscoveredCompetitions");
        }
    }
}
