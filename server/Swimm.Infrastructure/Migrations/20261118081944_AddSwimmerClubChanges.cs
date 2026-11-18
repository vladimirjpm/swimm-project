using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSwimmerClubChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ClubLocked",
                table: "Swimmers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "Sys_SwimmerClubChanges",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SwimmerId = table.Column<int>(type: "integer", nullable: false),
                    FromClubId = table.Column<int>(type: "integer", nullable: true),
                    ToClubId = table.Column<int>(type: "integer", nullable: true),
                    Source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    LastSwimDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ChangedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sys_SwimmerClubChanges", x => x.Id);
                    table.CheckConstraint("CK_Sys_SwimmerClubChanges_Source", "\"Source\" IN ('import', 'cli', 'admin')");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Sys_SwimmerClubChanges_ChangedAt",
                table: "Sys_SwimmerClubChanges",
                column: "ChangedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Sys_SwimmerClubChanges_SwimmerId_ChangedAt",
                table: "Sys_SwimmerClubChanges",
                columns: new[] { "SwimmerId", "ChangedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Sys_SwimmerClubChanges");

            migrationBuilder.DropColumn(
                name: "ClubLocked",
                table: "Swimmers");
        }
    }
}
