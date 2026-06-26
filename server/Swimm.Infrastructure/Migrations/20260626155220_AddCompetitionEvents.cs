using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCompetitionEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DayNumber",
                table: "Competitions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EventId",
                table: "Competitions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubName",
                table: "Competitions",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CompetitionEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: true),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompetitionEvents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Competitions_EventId",
                table: "Competitions",
                column: "EventId");

            migrationBuilder.AddForeignKey(
                name: "FK_Competitions_CompetitionEvents_EventId",
                table: "Competitions",
                column: "EventId",
                principalTable: "CompetitionEvents",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // Грант swimm_ro — CompetitionEvents читается анонимным public read-path.
            migrationBuilder.Sql("GRANT SELECT ON \"CompetitionEvents\" TO swimm_ro;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Competitions_CompetitionEvents_EventId",
                table: "Competitions");

            migrationBuilder.DropTable(
                name: "CompetitionEvents");

            migrationBuilder.DropIndex(
                name: "IX_Competitions_EventId",
                table: "Competitions");

            migrationBuilder.DropColumn(
                name: "DayNumber",
                table: "Competitions");

            migrationBuilder.DropColumn(
                name: "EventId",
                table: "Competitions");

            migrationBuilder.DropColumn(
                name: "SubName",
                table: "Competitions");
        }
    }
}
