using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UserStartListPlans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Sys_UserStartListPlans",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    OrgCompId = table.Column<int>(type: "integer", nullable: false),
                    SwimmerIds = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    ClubIds = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    ImComing = table.Column<bool>(type: "boolean", nullable: false),
                    NotifyMe = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sys_UserStartListPlans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Sys_UserStartListPlans_Sys_AppUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "Sys_AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Sys_UserStartListPlans_UserId_OrgCompId",
                table: "Sys_UserStartListPlans",
                columns: new[] { "UserId", "OrgCompId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Sys_UserStartListPlans");
        }
    }
}
