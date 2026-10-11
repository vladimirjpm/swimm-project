using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMediaReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ModerationState",
                table: "Sys_UserMedia",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Sys_MediaReports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserMediaId = table.Column<int>(type: "integer", nullable: false),
                    ReporterUserId = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Comment = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DecidedByUserId = table.Column<int>(type: "integer", nullable: true),
                    DecidedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sys_MediaReports", x => x.Id);
                    table.CheckConstraint("CK_MediaReports_OtherNeedsComment", "\"Reason\" <> 'other' OR (\"Comment\" IS NOT NULL AND length(btrim(\"Comment\")) > 0)");
                    table.CheckConstraint("CK_MediaReports_Reason", "\"Reason\" IN ('wrong_swimmer', 'inappropriate', 'spam', 'privacy', 'other')");
                    table.CheckConstraint("CK_MediaReports_Status", "\"Status\" IN ('open', 'kept', 'removed')");
                    table.ForeignKey(
                        name: "FK_Sys_MediaReports_Sys_AppUsers_ReporterUserId",
                        column: x => x.ReporterUserId,
                        principalTable: "Sys_AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Sys_MediaReports_Sys_UserMedia_UserMediaId",
                        column: x => x.UserMediaId,
                        principalTable: "Sys_UserMedia",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_UserMedia_ModerationState",
                table: "Sys_UserMedia",
                sql: "\"ModerationState\" IS NULL OR \"ModerationState\" IN ('under_review', 'removed')");

            migrationBuilder.CreateIndex(
                name: "IX_Sys_MediaReports_ReporterUserId",
                table: "Sys_MediaReports",
                column: "ReporterUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Sys_MediaReports_Status_UserMediaId",
                table: "Sys_MediaReports",
                columns: new[] { "Status", "UserMediaId" });

            migrationBuilder.CreateIndex(
                name: "IX_Sys_MediaReports_UserMediaId_ReporterUserId",
                table: "Sys_MediaReports",
                columns: new[] { "UserMediaId", "ReporterUserId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Sys_MediaReports");

            migrationBuilder.DropCheckConstraint(
                name: "CK_UserMedia_ModerationState",
                table: "Sys_UserMedia");

            migrationBuilder.DropColumn(
                name: "ModerationState",
                table: "Sys_UserMedia");
        }
    }
}
