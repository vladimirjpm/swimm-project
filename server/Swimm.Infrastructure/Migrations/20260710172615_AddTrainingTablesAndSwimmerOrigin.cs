using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTrainingTablesAndSwimmerOrigin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Origin",
                table: "Swimmers",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "isr");

            migrationBuilder.CreateTable(
                name: "Sys_TrainingSessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    HubGroupId = table.Column<int>(type: "integer", nullable: false),
                    ExternalTrainingId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PoolType = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sys_TrainingSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Sys_TrainingSessions_HubGroups_HubGroupId",
                        column: x => x.HubGroupId,
                        principalTable: "HubGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Sys_TrainingResults",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SessionId = table.Column<int>(type: "integer", nullable: false),
                    SwimmerId = table.Column<int>(type: "integer", nullable: false),
                    StyleId = table.Column<int>(type: "integer", nullable: false),
                    Distance = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Gender = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    TimeMillisecond = table.Column<int>(type: "integer", nullable: true),
                    TimeOriginal = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SetNo = table.Column<int>(type: "integer", nullable: false),
                    OrderNo = table.Column<int>(type: "integer", nullable: false),
                    IntervalSec = table.Column<int>(type: "integer", nullable: true),
                    Intensity = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    IsPaddles = table.Column<bool>(type: "boolean", nullable: false),
                    IsBuoy = table.Column<bool>(type: "boolean", nullable: false),
                    ExpectedTimeMs = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sys_TrainingResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Sys_TrainingResults_Styles_StyleId",
                        column: x => x.StyleId,
                        principalTable: "Styles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Sys_TrainingResults_Swimmers_SwimmerId",
                        column: x => x.SwimmerId,
                        principalTable: "Swimmers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Sys_TrainingResults_Sys_TrainingSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "Sys_TrainingSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Sys_TrainingResults_SessionId",
                table: "Sys_TrainingResults",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_Sys_TrainingResults_StyleId",
                table: "Sys_TrainingResults",
                column: "StyleId");

            migrationBuilder.CreateIndex(
                name: "IX_Sys_TrainingResults_SwimmerId",
                table: "Sys_TrainingResults",
                column: "SwimmerId");

            migrationBuilder.CreateIndex(
                name: "IX_Sys_TrainingSessions_HubGroupId_ExternalTrainingId",
                table: "Sys_TrainingSessions",
                columns: new[] { "HubGroupId", "ExternalTrainingId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Sys_TrainingResults");

            migrationBuilder.DropTable(
                name: "Sys_TrainingSessions");

            migrationBuilder.DropColumn(
                name: "Origin",
                table: "Swimmers");
        }
    }
}
