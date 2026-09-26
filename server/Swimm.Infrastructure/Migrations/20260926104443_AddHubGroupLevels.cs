using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddHubGroupLevels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Sys_HubGroupLevels",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    HubGroupId = table.Column<int>(type: "integer", nullable: false),
                    Rank = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Color = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sys_HubGroupLevels", x => x.Id);
                    table.UniqueConstraint("AK_Sys_HubGroupLevels_HubGroupId_Id", x => new { x.HubGroupId, x.Id });
                    table.ForeignKey(
                        name: "FK_Sys_HubGroupLevels_HubGroups_HubGroupId",
                        column: x => x.HubGroupId,
                        principalTable: "HubGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Sys_HubGroupSwimmerLevels",
                columns: table => new
                {
                    HubGroupId = table.Column<int>(type: "integer", nullable: false),
                    SwimmerId = table.Column<int>(type: "integer", nullable: false),
                    LevelId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sys_HubGroupSwimmerLevels", x => new { x.HubGroupId, x.SwimmerId });
                    table.ForeignKey(
                        name: "FK_Sys_HubGroupSwimmerLevels_HubGroups_HubGroupId",
                        column: x => x.HubGroupId,
                        principalTable: "HubGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Sys_HubGroupSwimmerLevels_Swimmers_SwimmerId",
                        column: x => x.SwimmerId,
                        principalTable: "Swimmers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Sys_HubGroupSwimmerLevels_Sys_HubGroupLevels_HubGroupId_Lev~",
                        columns: x => new { x.HubGroupId, x.LevelId },
                        principalTable: "Sys_HubGroupLevels",
                        principalColumns: new[] { "HubGroupId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Sys_HubGroupLevels_HubGroupId_Rank",
                table: "Sys_HubGroupLevels",
                columns: new[] { "HubGroupId", "Rank" });

            migrationBuilder.CreateIndex(
                name: "IX_Sys_HubGroupSwimmerLevels_HubGroupId_LevelId",
                table: "Sys_HubGroupSwimmerLevels",
                columns: new[] { "HubGroupId", "LevelId" });

            migrationBuilder.CreateIndex(
                name: "IX_Sys_HubGroupSwimmerLevels_SwimmerId",
                table: "Sys_HubGroupSwimmerLevels",
                column: "SwimmerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Sys_HubGroupSwimmerLevels");

            migrationBuilder.DropTable(
                name: "Sys_HubGroupLevels");
        }
    }
}
