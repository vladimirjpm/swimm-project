using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <summary>
    /// Серии турниров и официальное награждение по регламенту (docs/plans/official-awards-plan.md §5):
    /// шесть публичных таблиц + привязка дня к части серии и отметка «регламент подтверждён».
    /// </summary>
    public partial class AddCompetitionSeries : Migration
    {
        /// <summary>
        /// Новые таблицы из <c>server/db/seed-tables.txt</c> — синхронизация «прод → локально» везёт
        /// их вместе с соревнованиями (серии заводит Влад в ProdAdmin). Триггер версии ставится здесь,
        /// снимок <see cref="DataVersionForSync.Tables"/> не правится; сверяет DataVersionPgTests.
        /// </summary>
        internal static readonly string[] SyncTables =
        [
            "CompetitionSeries", "CompetitionSeriesParts", "SeriesRegulations",
            "SeriesAwards", "SeriesAwardBands", "SeriesAwardPrizes",
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SeriesPartId",
                table: "Competitions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SeriesRegulationCheckedAt",
                table: "Competitions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SeriesRegulationCheckedBy",
                table: "Competitions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CompetitionSeries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    NameEn = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    MatchPhrases = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompetitionSeries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CompetitionSeriesParts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SeriesId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    MinSeasonAge = table.Column<int>(type: "integer", nullable: true),
                    MaxSeasonAge = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompetitionSeriesParts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CompetitionSeriesParts_CompetitionSeries_SeriesId",
                        column: x => x.SeriesId,
                        principalTable: "CompetitionSeries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SeriesRegulations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SeriesId = table.Column<int>(type: "integer", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    SourceUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeriesRegulations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SeriesRegulations_CompetitionSeries_SeriesId",
                        column: x => x.SeriesId,
                        principalTable: "CompetitionSeries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SeriesAwards",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RegulationId = table.Column<int>(type: "integer", nullable: false),
                    PartId = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CountBest = table.Column<int>(type: "integer", nullable: true),
                    SplitByGender = table.Column<bool>(type: "boolean", nullable: false),
                    Places = table.Column<int>(type: "integer", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeriesAwards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SeriesAwards_CompetitionSeriesParts_PartId",
                        column: x => x.PartId,
                        principalTable: "CompetitionSeriesParts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SeriesAwards_SeriesRegulations_RegulationId",
                        column: x => x.RegulationId,
                        principalTable: "SeriesRegulations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SeriesAwardBands",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AwardId = table.Column<int>(type: "integer", nullable: false),
                    Gender = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    MinSeasonAge = table.Column<int>(type: "integer", nullable: true),
                    MaxSeasonAge = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeriesAwardBands", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SeriesAwardBands_SeriesAwards_AwardId",
                        column: x => x.AwardId,
                        principalTable: "SeriesAwards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SeriesAwardPrizes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AwardId = table.Column<int>(type: "integer", nullable: false),
                    Place = table.Column<int>(type: "integer", nullable: false),
                    Text = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeriesAwardPrizes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SeriesAwardPrizes_SeriesAwards_AwardId",
                        column: x => x.AwardId,
                        principalTable: "SeriesAwards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Competitions_SeriesPartId",
                table: "Competitions",
                column: "SeriesPartId");

            migrationBuilder.CreateIndex(
                name: "IX_CompetitionSeries_Name",
                table: "CompetitionSeries",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompetitionSeriesParts_SeriesId_Name",
                table: "CompetitionSeriesParts",
                columns: new[] { "SeriesId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SeriesAwardBands_AwardId",
                table: "SeriesAwardBands",
                column: "AwardId");

            migrationBuilder.CreateIndex(
                name: "IX_SeriesAwardPrizes_AwardId_Place",
                table: "SeriesAwardPrizes",
                columns: new[] { "AwardId", "Place" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SeriesAwards_PartId",
                table: "SeriesAwards",
                column: "PartId");

            migrationBuilder.CreateIndex(
                name: "IX_SeriesAwards_RegulationId",
                table: "SeriesAwards",
                column: "RegulationId");

            migrationBuilder.CreateIndex(
                name: "IX_SeriesRegulations_SeriesId_EffectiveFrom",
                table: "SeriesRegulations",
                columns: new[] { "SeriesId", "EffectiveFrom" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Competitions_CompetitionSeriesParts_SeriesPartId",
                table: "Competitions",
                column: "SeriesPartId",
                principalTable: "CompetitionSeriesParts",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // Грант swimm_ro: награды читает ПУБЛИЧНЫЙ путь Overview (SwimmReadDbContext).
            // Приватного в таблицах нет — регламент турнира публичен. Роли может не быть (чистая
            // БД, CI) — тогда пропуск; базовый набор грантов — server/db/02-grants.sql.
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF EXISTS (SELECT FROM pg_roles WHERE rolname = 'swimm_ro') THEN
                        GRANT SELECT ON ""CompetitionSeries"", ""CompetitionSeriesParts"", ""SeriesRegulations"",
                                        ""SeriesAwards"", ""SeriesAwardBands"", ""SeriesAwardPrizes"" TO swimm_ro;
                    END IF;
                END$$;
            ");

            // Грант swimm_sync: таблицы едут в синхронизацию «прод → локально» (seed-tables.txt).
            // 02-grants.sql выдал бы их сам по триггеру, но на проде после --migrate его никто не
            // перезапускает — без этого /db-sync-from-prod упал бы на первой же новой таблице.
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF EXISTS (SELECT FROM pg_roles WHERE rolname = 'swimm_sync') THEN
                        GRANT SELECT ON ""CompetitionSeries"", ""CompetitionSeriesParts"", ""SeriesRegulations"",
                                        ""SeriesAwards"", ""SeriesAwardBands"", ""SeriesAwardPrizes"" TO swimm_sync;
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
                name: "FK_Competitions_CompetitionSeriesParts_SeriesPartId",
                table: "Competitions");

            migrationBuilder.DropTable(
                name: "SeriesAwardBands");

            migrationBuilder.DropTable(
                name: "SeriesAwardPrizes");

            migrationBuilder.DropTable(
                name: "SeriesAwards");

            migrationBuilder.DropTable(
                name: "CompetitionSeriesParts");

            migrationBuilder.DropTable(
                name: "SeriesRegulations");

            migrationBuilder.DropTable(
                name: "CompetitionSeries");

            migrationBuilder.DropIndex(
                name: "IX_Competitions_SeriesPartId",
                table: "Competitions");

            migrationBuilder.DropColumn(
                name: "SeriesPartId",
                table: "Competitions");

            migrationBuilder.DropColumn(
                name: "SeriesRegulationCheckedAt",
                table: "Competitions");

            migrationBuilder.DropColumn(
                name: "SeriesRegulationCheckedBy",
                table: "Competitions");
        }
    }
}
