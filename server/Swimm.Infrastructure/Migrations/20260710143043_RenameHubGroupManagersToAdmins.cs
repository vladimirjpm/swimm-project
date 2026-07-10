using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RenameHubGroupManagersToAdmins : Migration
    {
        /// <inheritdoc />
        // Переименование Sys_HubGroupManagers → Sys_HubGroupAdmins («со-тренер» → «админ группы»).
        // EF генерит Drop+Create (не умеет распознать rename сущности). Данных нет — фича не
        // выпущена, ни одного админа группы ещё не заведено, — поэтому потери данных нулевые, а
        // имена всех constraint/index консистентны со снапшотом. См. HubGroupAdmin.
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Sys_HubGroupManagers");

            migrationBuilder.CreateTable(
                name: "Sys_HubGroupAdmins",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    HubGroupId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    GrantedByUserId = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sys_HubGroupAdmins", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Sys_HubGroupAdmins_HubGroups_HubGroupId",
                        column: x => x.HubGroupId,
                        principalTable: "HubGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Sys_HubGroupAdmins_Sys_AppUsers_GrantedByUserId",
                        column: x => x.GrantedByUserId,
                        principalTable: "Sys_AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Sys_HubGroupAdmins_Sys_AppUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "Sys_AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Sys_HubGroupAdmins_GrantedByUserId",
                table: "Sys_HubGroupAdmins",
                column: "GrantedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Sys_HubGroupAdmins_HubGroupId_UserId",
                table: "Sys_HubGroupAdmins",
                columns: new[] { "HubGroupId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sys_HubGroupAdmins_UserId",
                table: "Sys_HubGroupAdmins",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Sys_HubGroupAdmins");

            migrationBuilder.CreateTable(
                name: "Sys_HubGroupManagers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GrantedByUserId = table.Column<int>(type: "integer", nullable: false),
                    HubGroupId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sys_HubGroupManagers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Sys_HubGroupManagers_HubGroups_HubGroupId",
                        column: x => x.HubGroupId,
                        principalTable: "HubGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Sys_HubGroupManagers_Sys_AppUsers_GrantedByUserId",
                        column: x => x.GrantedByUserId,
                        principalTable: "Sys_AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Sys_HubGroupManagers_Sys_AppUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "Sys_AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Sys_HubGroupManagers_GrantedByUserId",
                table: "Sys_HubGroupManagers",
                column: "GrantedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Sys_HubGroupManagers_HubGroupId_UserId",
                table: "Sys_HubGroupManagers",
                columns: new[] { "HubGroupId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sys_HubGroupManagers_UserId",
                table: "Sys_HubGroupManagers",
                column: "UserId");
        }
    }
}
