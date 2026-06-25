using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUserFavoritesAndUserMedia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Sys_UserFavorites",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    TargetType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SwimmerId = table.Column<int>(type: "integer", nullable: true),
                    ClubId = table.Column<int>(type: "integer", nullable: true),
                    IsPrimary = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sys_UserFavorites", x => x.Id);
                    table.CheckConstraint("CK_UserFav_TargetType", "(\"TargetType\" = 'swimmer' AND \"SwimmerId\" IS NOT NULL AND \"ClubId\" IS NULL) OR (\"TargetType\" = 'club'    AND \"ClubId\"    IS NOT NULL AND \"SwimmerId\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_Sys_UserFavorites_Clubs_ClubId",
                        column: x => x.ClubId,
                        principalTable: "Clubs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Sys_UserFavorites_Swimmers_SwimmerId",
                        column: x => x.SwimmerId,
                        principalTable: "Swimmers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Sys_UserFavorites_Sys_AppUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "Sys_AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Sys_UserMedia",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    SwimmerId = table.Column<int>(type: "integer", nullable: false),
                    ResultId = table.Column<long>(type: "bigint", nullable: true),
                    CompetitionId = table.Column<int>(type: "integer", nullable: true),
                    Level = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    MediaType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SourceType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Visibility = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sys_UserMedia", x => x.Id);
                    table.CheckConstraint("CK_UserMedia_Level", "\"Level\" IN ('swimmer', 'competition', 'result')");
                    table.CheckConstraint("CK_UserMedia_Visibility", "\"Visibility\" IN ('private', 'public')");
                    table.ForeignKey(
                        name: "FK_Sys_UserMedia_Competitions_CompetitionId",
                        column: x => x.CompetitionId,
                        principalTable: "Competitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Sys_UserMedia_Results_ResultId",
                        column: x => x.ResultId,
                        principalTable: "Results",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Sys_UserMedia_Swimmers_SwimmerId",
                        column: x => x.SwimmerId,
                        principalTable: "Swimmers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Sys_UserMedia_Sys_AppUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "Sys_AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Sys_UserFavorites_ClubId",
                table: "Sys_UserFavorites",
                column: "ClubId");

            migrationBuilder.CreateIndex(
                name: "IX_Sys_UserFavorites_SwimmerId",
                table: "Sys_UserFavorites",
                column: "SwimmerId");

            migrationBuilder.CreateIndex(
                name: "IX_Sys_UserFavorites_UserId",
                table: "Sys_UserFavorites",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Sys_UserMedia_CompetitionId",
                table: "Sys_UserMedia",
                column: "CompetitionId");

            migrationBuilder.CreateIndex(
                name: "IX_Sys_UserMedia_ResultId",
                table: "Sys_UserMedia",
                column: "ResultId");

            migrationBuilder.CreateIndex(
                name: "IX_Sys_UserMedia_SwimmerId",
                table: "Sys_UserMedia",
                column: "SwimmerId");

            migrationBuilder.CreateIndex(
                name: "IX_Sys_UserMedia_UserId",
                table: "Sys_UserMedia",
                column: "UserId");

            // Partial unique indexes — PostgreSQL only, поэтому через raw SQL.
            // UX_UserFav_OnePrimary: один primary-swimmer на пользователя.
            migrationBuilder.Sql(
                @"CREATE UNIQUE INDEX ""UX_UserFav_OnePrimary""
                  ON ""Sys_UserFavorites"" (""UserId"")
                  WHERE ""IsPrimary"" = true AND ""TargetType"" = 'swimmer';");

            // UX_UserFav_Swimmer: пловец не может быть дважды в фаворитах одного пользователя.
            migrationBuilder.Sql(
                @"CREATE UNIQUE INDEX ""UX_UserFav_Swimmer""
                  ON ""Sys_UserFavorites"" (""UserId"", ""SwimmerId"")
                  WHERE ""TargetType"" = 'swimmer';");

            // UX_UserFav_Club: клуб не может быть дважды в фаворитах одного пользователя.
            migrationBuilder.Sql(
                @"CREATE UNIQUE INDEX ""UX_UserFav_Club""
                  ON ""Sys_UserFavorites"" (""UserId"", ""ClubId"")
                  WHERE ""TargetType"" = 'club';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""UX_UserFav_OnePrimary"";");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""UX_UserFav_Swimmer"";");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""UX_UserFav_Club"";");

            migrationBuilder.DropTable(
                name: "Sys_UserFavorites");

            migrationBuilder.DropTable(
                name: "Sys_UserMedia");
        }
    }
}
