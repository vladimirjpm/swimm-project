using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSwimmerLogligClubs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Sys_SwimmerLogligClubs",
                columns: table => new
                {
                    SwimmerId = table.Column<int>(type: "integer", nullable: false),
                    LogligId = table.Column<int>(type: "integer", nullable: false),
                    ClubName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CheckedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sys_SwimmerLogligClubs", x => x.SwimmerId);
                });

            // Р112: ответы loglig, которые сверка кандидатов (Р98) уже получила, — в общее место,
            // чтобы колонка «loglig» на /Admin/Clubs не начинала с нуля. Только удачные ответы
            // (CheckedAt и loglig-id есть, клуб на карточке напечатан), как и пишет код дальше.
            // Sys_-таблица: grant swimm_ro не нужен, swimm_rw получает права по default privileges.
            migrationBuilder.Sql("""
                INSERT INTO "Sys_SwimmerLogligClubs" ("SwimmerId", "LogligId", "ClubName", "CheckedAt")
                SELECT "SwimmerId", "LogligId", "LogligClubName", "CheckedAt"
                FROM "Sys_SwimmerClubCandidates"
                WHERE "LogligId" IS NOT NULL AND "CheckedAt" IS NOT NULL
                  AND "LogligClubName" IS NOT NULL AND btrim("LogligClubName") <> '';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Sys_SwimmerLogligClubs");
        }
    }
}
