using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CompetitionMeetInfo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CompetitionMeetInfos",
                columns: table => new
                {
                    OrgCompId = table.Column<int>(type: "integer", nullable: false),
                    IsChampionship = table.Column<bool>(type: "boolean", nullable: false),
                    IsChampionshipOverride = table.Column<bool>(type: "boolean", nullable: true),
                    RegulationUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    RegulationCheckedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompetitionMeetInfos", x => x.OrgCompId);
                });

            migrationBuilder.CreateTable(
                name: "CompetitionWarmUps",
                columns: table => new
                {
                    OrgCompId = table.Column<int>(type: "integer", nullable: false),
                    Date = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    WarmUpAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompetitionWarmUps", x => new { x.OrgCompId, x.Date });
                    table.ForeignKey(
                        name: "FK_CompetitionWarmUps_CompetitionMeetInfos_OrgCompId",
                        column: x => x.OrgCompId,
                        principalTable: "CompetitionMeetInfos",
                        principalColumn: "OrgCompId",
                        onDelete: ReferentialAction.Cascade);
                });

            // Грант swimm_ro: обе таблицы читает ПУБЛИЧНЫЙ путь стартового протокола
            // (SwimmReadDbContext) — из них таб Start list считает «во сколько приезжать».
            // Приватного в них нет: чемпионат виден в регламенте, разминка — расписание.
            // Роль может отсутствовать (чистая БД, CI, дев без setup-roles) — тогда грант
            // молча пропускаем, иначе `--migrate` падает на пустой базе. Базовый набор
            // грантов — server/db/02-grants.sql.
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF EXISTS (SELECT FROM pg_roles WHERE rolname = 'swimm_ro') THEN
                        GRANT SELECT ON ""CompetitionMeetInfos"" TO swimm_ro;
                        GRANT SELECT ON ""CompetitionWarmUps"" TO swimm_ro;
                    END IF;
                END$$;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CompetitionWarmUps");

            migrationBuilder.DropTable(
                name: "CompetitionMeetInfos");
        }
    }
}
