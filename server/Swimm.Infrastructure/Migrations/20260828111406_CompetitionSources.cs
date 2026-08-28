using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CompetitionSources : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CompetitionSources",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompetitionId = table.Column<int>(type: "integer", nullable: false),
                    OrgCompId = table.Column<int>(type: "integer", nullable: false),
                    SourceDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    SourceName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompetitionSources", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CompetitionSources_Competitions_CompetitionId",
                        column: x => x.CompetitionId,
                        principalTable: "Competitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CompetitionSources_CompetitionId_OrgCompId",
                table: "CompetitionSources",
                columns: new[] { "CompetitionId", "OrgCompId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompetitionSources_OrgCompId",
                table: "CompetitionSources",
                column: "OrgCompId");

            // Грант swimm_ro: таблицу читает овервью соревнования на публичном пути
            // (SwimmReadDbContext) — из неё строятся подтабы таба Start list. Приватного
            // в ней нет: это те же compID, что стоят в адресе на isr.org.il.
            // Роль может отсутствовать (чистая БД, CI, дев без setup-roles) — тогда грант
            // молча пропускаем, иначе `--migrate` падает на пустой базе. Базовый набор
            // грантов — server/db/02-grants.sql.
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF EXISTS (SELECT FROM pg_roles WHERE rolname = 'swimm_ro') THEN
                        GRANT SELECT ON ""CompetitionSources"" TO swimm_ro;
                    END IF;
                END$$;
            ");

            // Бэкфилл: у соревнований со штампом OrgCompId источник ровно один и он известен
            // — заводим привязки сразу, чтобы таб Start list не пропал у тех, кто уже работал
            // на скалярном поле. Составные (окружные) чемпионаты дособираются руками.
            migrationBuilder.Sql(@"
                INSERT INTO ""CompetitionSources"" (""CompetitionId"", ""OrgCompId"", ""SourceDate"", ""SourceName"", ""SortOrder"")
                SELECT c.""Id"", c.""OrgCompId"",
                       to_date(c.""Date"", 'DD/MM/YYYY'),
                       COALESCE(c.""SubName"", c.""Name""),
                       0
                FROM ""Competitions"" c
                WHERE c.""OrgCompId"" IS NOT NULL
                ON CONFLICT DO NOTHING;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CompetitionSources");
        }
    }
}
