using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <summary>
    /// Значок серии турнира байтами в базе (docs/plans/series-emblem-plan.md): публичная таблица
    /// SeriesEmblems и необязательная ссылка на неё у серии (SET NULL — удаление значка серию не ломает).
    /// </summary>
    public partial class AddSeriesEmblems : Migration
    {
        /// <summary>
        /// Новая таблица из <c>server/db/seed-tables.txt</c>: значки заводит Влад в ProdAdmin, локальная
        /// база получает их синхронизацией. Триггер версии ставится здесь; сверяет DataVersionPgTests.
        /// </summary>
        internal static readonly string[] SyncTables = ["SeriesEmblems"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "EmblemId",
                table: "CompetitionSeries",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SeriesEmblems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Data = table.Column<byte[]>(type: "bytea", nullable: false),
                    ContentType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Width = table.Column<int>(type: "integer", nullable: false),
                    Height = table.Column<int>(type: "integer", nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeriesEmblems", x => x.Id);
                    table.CheckConstraint("CK_SeriesEmblems_ContentType", "\"ContentType\" IN ('image/png', 'image/webp')");
                    table.CheckConstraint("CK_SeriesEmblems_DataSize", "octet_length(\"Data\") BETWEEN 1 AND 307200");
                    table.CheckConstraint("CK_SeriesEmblems_Dimensions", "\"Width\" BETWEEN 1 AND 1024 AND \"Height\" BETWEEN 1 AND 1024");
                    table.CheckConstraint("CK_SeriesEmblems_Sha256", "\"Sha256\" ~ '^[0-9a-f]{64}$'");
                });

            migrationBuilder.CreateIndex(
                name: "IX_CompetitionSeries_EmblemId",
                table: "CompetitionSeries",
                column: "EmblemId");

            migrationBuilder.AddForeignKey(
                name: "FK_CompetitionSeries_SeriesEmblems_EmblemId",
                table: "CompetitionSeries",
                column: "EmblemId",
                principalTable: "SeriesEmblems",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // Грант swimm_ro: картинку отдаёт ПУБЛИЧНЫЙ адрес /api/series-emblems (SwimmReadDbContext).
            // Роли может не быть (чистая БД, CI) — тогда пропуск; базовый набор — server/db/02-grants.sql.
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF EXISTS (SELECT FROM pg_roles WHERE rolname = 'swimm_ro') THEN
                        GRANT SELECT ON ""SeriesEmblems"" TO swimm_ro;
                    END IF;
                END$$;
            ");

            // Грант swimm_sync: таблица едет в синхронизацию «прод → локально» (seed-tables.txt).
            // На проде после --migrate 02-grants.sql никто не перезапускает — без этого
            // /db-sync-from-prod упал бы на новой таблице.
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF EXISTS (SELECT FROM pg_roles WHERE rolname = 'swimm_sync') THEN
                        GRANT SELECT ON ""SeriesEmblems"" TO swimm_sync;
                        GRANT SELECT ON ALL SEQUENCES IN SCHEMA public TO swimm_sync;
                    END IF;
                END$$;
            ");

            // Версия данных для синхронизации — как в DataVersionForSync.
            foreach (var t in SyncTables)
                migrationBuilder.Sql($"""
                    INSERT INTO "Sys_DataVersion" ("Table") VALUES ('{t}') ON CONFLICT DO NOTHING;
                    CREATE TRIGGER "Sys_DataVersion_Bump"
                        AFTER INSERT OR UPDATE OR DELETE OR TRUNCATE ON "{t}"
                        FOR EACH STATEMENT EXECUTE FUNCTION "Sys_BumpDataVersion"();
                    """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var t in SyncTables)
                migrationBuilder.Sql($"""DELETE FROM "Sys_DataVersion" WHERE "Table" = '{t}';""");

            migrationBuilder.DropForeignKey(
                name: "FK_CompetitionSeries_SeriesEmblems_EmblemId",
                table: "CompetitionSeries");

            migrationBuilder.DropTable(
                name: "SeriesEmblems");

            migrationBuilder.DropIndex(
                name: "IX_CompetitionSeries_EmblemId",
                table: "CompetitionSeries");

            migrationBuilder.DropColumn(
                name: "EmblemId",
                table: "CompetitionSeries");
        }
    }
}
