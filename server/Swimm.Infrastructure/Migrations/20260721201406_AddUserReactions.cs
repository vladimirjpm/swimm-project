using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUserReactions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Sys_UserReactions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    MediaId = table.Column<int>(type: "integer", nullable: true),
                    ResultId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sys_UserReactions", x => x.Id);
                    table.CheckConstraint("CK_UserReactions_Kind", "(\"Kind\" = 'like'     AND \"MediaId\"  IS NOT NULL AND \"ResultId\" IS NULL) OR (\"Kind\" = 'congrats' AND \"ResultId\" IS NOT NULL AND \"MediaId\"  IS NULL)");
                    table.ForeignKey(
                        name: "FK_Sys_UserReactions_Results_ResultId",
                        column: x => x.ResultId,
                        principalTable: "Results",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Sys_UserReactions_Sys_AppUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "Sys_AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Sys_UserReactions_Sys_UserMedia_MediaId",
                        column: x => x.MediaId,
                        principalTable: "Sys_UserMedia",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Sys_UserReactions_MediaId",
                table: "Sys_UserReactions",
                column: "MediaId");

            migrationBuilder.CreateIndex(
                name: "IX_Sys_UserReactions_ResultId",
                table: "Sys_UserReactions",
                column: "ResultId");

            migrationBuilder.CreateIndex(
                name: "IX_Sys_UserReactions_UserId",
                table: "Sys_UserReactions",
                column: "UserId");

            // Partial unique indexes — PostgreSQL only, поэтому через raw SQL.
            // Одна реакция на пользователя+цель; они же покрывают COUNT(*) по цели
            // (IX_MediaId/IX_ResultId выше остаются для FK-каскадов).
            migrationBuilder.Sql(
                @"CREATE UNIQUE INDEX ""UX_UserReactions_Like""
                  ON ""Sys_UserReactions"" (""UserId"", ""MediaId"")
                  WHERE ""Kind"" = 'like';");

            migrationBuilder.Sql(
                @"CREATE UNIQUE INDEX ""UX_UserReactions_Congrats""
                  ON ""Sys_UserReactions"" (""UserId"", ""ResultId"")
                  WHERE ""Kind"" = 'congrats';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Sys_UserReactions");
        }
    }
}
