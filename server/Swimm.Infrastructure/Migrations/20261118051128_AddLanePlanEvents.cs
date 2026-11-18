using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLanePlanEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Sys_LanePlanEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PlanId = table.Column<int>(type: "integer", nullable: false),
                    At = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SwimmerId = table.Column<int>(type: "integer", nullable: true),
                    FromLane = table.Column<int>(type: "integer", nullable: true),
                    ToLane = table.Column<int>(type: "integer", nullable: true),
                    OnLanes = table.Column<int>(type: "integer", nullable: true),
                    ActorUserId = table.Column<int>(type: "integer", nullable: true),
                    ByCoach = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sys_LanePlanEvents", x => x.Id);
                    table.CheckConstraint("CK_LanePlanEvents_Kind", "\"Kind\" IN ('published', 'going', 'cant_come', 'moved')");
                    table.CheckConstraint("CK_LanePlanEvents_Lanes", "(\"FromLane\" IS NULL OR \"FromLane\" >= 0) AND (\"ToLane\" IS NULL OR \"ToLane\" >= 0)");
                    table.ForeignKey(
                        name: "FK_Sys_LanePlanEvents_Swimmers_SwimmerId",
                        column: x => x.SwimmerId,
                        principalTable: "Swimmers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Sys_LanePlanEvents_Sys_AppUsers_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Sys_AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Sys_LanePlanEvents_Sys_LanePlans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "Sys_LanePlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Sys_LanePlanEvents_ActorUserId",
                table: "Sys_LanePlanEvents",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Sys_LanePlanEvents_PlanId_At",
                table: "Sys_LanePlanEvents",
                columns: new[] { "PlanId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_Sys_LanePlanEvents_SwimmerId",
                table: "Sys_LanePlanEvents",
                column: "SwimmerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Sys_LanePlanEvents");
        }
    }
}
