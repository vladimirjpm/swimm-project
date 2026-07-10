using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddHubGroupOfficialClubs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_HubGroups_ClubId",
                table: "HubGroups");

            migrationBuilder.AddColumn<bool>(
                name: "IsOfficial",
                table: "HubGroups",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "Sys_HubGroupClubRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    HubGroupId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    ClubId = table.Column<int>(type: "integer", nullable: false),
                    Message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DecidedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DecidedByUserId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sys_HubGroupClubRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Sys_HubGroupClubRequests_Clubs_ClubId",
                        column: x => x.ClubId,
                        principalTable: "Clubs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Sys_HubGroupClubRequests_HubGroups_HubGroupId",
                        column: x => x.HubGroupId,
                        principalTable: "HubGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Sys_HubGroupClubRequests_Sys_AppUsers_DecidedByUserId",
                        column: x => x.DecidedByUserId,
                        principalTable: "Sys_AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Sys_HubGroupClubRequests_Sys_AppUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "Sys_AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HubGroups_ClubId_Official",
                table: "HubGroups",
                column: "ClubId",
                unique: true,
                filter: "\"IsOfficial\"");

            migrationBuilder.AddCheckConstraint(
                name: "CK_HubGroups_OfficialRequiresClub",
                table: "HubGroups",
                sql: "NOT \"IsOfficial\" OR \"ClubId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Sys_HubGroupClubRequests_ClubId",
                table: "Sys_HubGroupClubRequests",
                column: "ClubId");

            migrationBuilder.CreateIndex(
                name: "IX_Sys_HubGroupClubRequests_DecidedByUserId",
                table: "Sys_HubGroupClubRequests",
                column: "DecidedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Sys_HubGroupClubRequests_HubGroupId_Pending",
                table: "Sys_HubGroupClubRequests",
                column: "HubGroupId",
                unique: true,
                filter: "\"Status\" = 'pending'");

            migrationBuilder.CreateIndex(
                name: "IX_Sys_HubGroupClubRequests_UserId",
                table: "Sys_HubGroupClubRequests",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Sys_HubGroupClubRequests");

            migrationBuilder.DropIndex(
                name: "IX_HubGroups_ClubId_Official",
                table: "HubGroups");

            migrationBuilder.DropCheckConstraint(
                name: "CK_HubGroups_OfficialRequiresClub",
                table: "HubGroups");

            migrationBuilder.DropColumn(
                name: "IsOfficial",
                table: "HubGroups");

            migrationBuilder.CreateIndex(
                name: "IX_HubGroups_ClubId",
                table: "HubGroups",
                column: "ClubId");
        }
    }
}
