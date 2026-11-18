using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <summary>
    /// Прежние названия клубов (docs/plans/swimmer-club-change-plan.md §5, Н1 + Н3): публичная
    /// таблица ClubNames и ссылка на неё у строки результата — под каким названием строка
    /// напечатана. Пустая на старте: строки заводит склейка видом «Переименование».
    /// </summary>
    public partial class AddClubNames : Migration
    {
        /// <summary>
        /// Новая таблица из <c>server/db/seed-tables.txt</c>: склейку на проде делает Влад в ProdAdmin,
        /// локальная база получает названия синхронизацией. Триггер версии — здесь; сверяет DataVersionPgTests.
        /// </summary>
        internal static readonly string[] SyncTables = ["ClubNames"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ClubNameId",
                table: "Results",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ClubNames",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ClubId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    UsedFrom = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    UsedTo = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    MergedClubId = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClubNames", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClubNames_Clubs_ClubId",
                        column: x => x.ClubId,
                        principalTable: "Clubs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClubNames_Clubs_MergedClubId",
                        column: x => x.MergedClubId,
                        principalTable: "Clubs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Results_ClubNameId",
                table: "Results",
                column: "ClubNameId");

            migrationBuilder.CreateIndex(
                name: "IX_ClubNames_ClubId",
                table: "ClubNames",
                column: "ClubId");

            migrationBuilder.CreateIndex(
                name: "IX_ClubNames_MergedClubId",
                table: "ClubNames",
                column: "MergedClubId");

            migrationBuilder.CreateIndex(
                name: "IX_ClubNames_Name",
                table: "ClubNames",
                column: "Name");

            migrationBuilder.AddForeignKey(
                name: "FK_Results_ClubNames_ClubNameId",
                table: "Results",
                column: "ClubNameId",
                principalTable: "ClubNames",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // Грант swimm_ro: прежние названия читает публичная страница клуба (SwimmReadDbContext).
            // Роли может не быть (чистая БД, CI) — тогда пропуск; базовый набор — server/db/02-grants.sql.
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF EXISTS (SELECT FROM pg_roles WHERE rolname = 'swimm_ro') THEN
                        GRANT SELECT ON ""ClubNames"" TO swimm_ro;
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
                        GRANT SELECT ON ""ClubNames"" TO swimm_sync;
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
                name: "FK_Results_ClubNames_ClubNameId",
                table: "Results");

            migrationBuilder.DropTable(
                name: "ClubNames");

            migrationBuilder.DropIndex(
                name: "IX_Results_ClubNameId",
                table: "Results");

            migrationBuilder.DropColumn(
                name: "ClubNameId",
                table: "Results");
        }
    }
}
