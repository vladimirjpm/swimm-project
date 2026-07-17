using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UserMediaPublications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Sys_UserMediaPublications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserMediaId = table.Column<int>(type: "integer", nullable: false),
                    HubGroupId = table.Column<int>(type: "integer", nullable: false),
                    Level = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DecidedByUserId = table.Column<int>(type: "integer", nullable: true),
                    DecidedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sys_UserMediaPublications", x => x.Id);
                    table.CheckConstraint("CK_UserMediaPublications_Level", "\"Level\" IN ('members', 'public')");
                    table.CheckConstraint("CK_UserMediaPublications_Status", "\"Status\" IN ('pending', 'approved', 'rejected')");
                    table.ForeignKey(
                        name: "FK_Sys_UserMediaPublications_HubGroups_HubGroupId",
                        column: x => x.HubGroupId,
                        principalTable: "HubGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Sys_UserMediaPublications_Sys_AppUsers_DecidedByUserId",
                        column: x => x.DecidedByUserId,
                        principalTable: "Sys_AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Sys_UserMediaPublications_Sys_UserMedia_UserMediaId",
                        column: x => x.UserMediaId,
                        principalTable: "Sys_UserMedia",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Sys_UserMediaPublications_DecidedByUserId",
                table: "Sys_UserMediaPublications",
                column: "DecidedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Sys_UserMediaPublications_HubGroupId",
                table: "Sys_UserMediaPublications",
                column: "HubGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_Sys_UserMediaPublications_UserMediaId_HubGroupId",
                table: "Sys_UserMediaPublications",
                columns: new[] { "UserMediaId", "HubGroupId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Sys_UserMediaPublications");
        }
    }
}
