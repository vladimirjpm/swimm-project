using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLanePlans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Sys_LanePlans",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    HubGroupId = table.Column<int>(type: "integer", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    LaneCount = table.Column<int>(type: "integer", nullable: false),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sys_LanePlans", x => x.Id);
                    table.CheckConstraint("CK_LanePlans_LaneCount", "\"LaneCount\" BETWEEN 1 AND 12");
                    table.CheckConstraint("CK_LanePlans_Status", "\"Status\" IN ('draft', 'published')");
                    table.ForeignKey(
                        name: "FK_Sys_LanePlans_HubGroups_HubGroupId",
                        column: x => x.HubGroupId,
                        principalTable: "HubGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Sys_LanePlans_Sys_AppUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Sys_AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Sys_LanePlanLanes",
                columns: table => new
                {
                    PlanId = table.Column<int>(type: "integer", nullable: false),
                    LaneNo = table.Column<int>(type: "integer", nullable: false),
                    LevelId = table.Column<int>(type: "integer", nullable: true),
                    Workout = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sys_LanePlanLanes", x => new { x.PlanId, x.LaneNo });
                    table.CheckConstraint("CK_LanePlanLanes_LaneNo", "\"LaneNo\" >= 1");
                    table.ForeignKey(
                        name: "FK_Sys_LanePlanLanes_Sys_HubGroupLevels_LevelId",
                        column: x => x.LevelId,
                        principalTable: "Sys_HubGroupLevels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Sys_LanePlanLanes_Sys_LanePlans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "Sys_LanePlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Sys_LanePlanSwimmers",
                columns: table => new
                {
                    PlanId = table.Column<int>(type: "integer", nullable: false),
                    SwimmerId = table.Column<int>(type: "integer", nullable: false),
                    LaneNo = table.Column<int>(type: "integer", nullable: true),
                    OrderNo = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sys_LanePlanSwimmers", x => new { x.PlanId, x.SwimmerId });
                    table.CheckConstraint("CK_LanePlanSwimmers_LaneNo", "\"LaneNo\" IS NULL OR \"LaneNo\" >= 1");
                    table.ForeignKey(
                        name: "FK_Sys_LanePlanSwimmers_Swimmers_SwimmerId",
                        column: x => x.SwimmerId,
                        principalTable: "Swimmers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Sys_LanePlanSwimmers_Sys_LanePlans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "Sys_LanePlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Sys_LanePlanLanes_LevelId",
                table: "Sys_LanePlanLanes",
                column: "LevelId");

            migrationBuilder.CreateIndex(
                name: "IX_Sys_LanePlans_CreatedByUserId",
                table: "Sys_LanePlans",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Sys_LanePlans_HubGroupId_Date",
                table: "Sys_LanePlans",
                columns: new[] { "HubGroupId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sys_LanePlanSwimmers_SwimmerId",
                table: "Sys_LanePlanSwimmers",
                column: "SwimmerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Sys_LanePlanLanes");

            migrationBuilder.DropTable(
                name: "Sys_LanePlanSwimmers");

            migrationBuilder.DropTable(
                name: "Sys_LanePlans");
        }
    }
}
