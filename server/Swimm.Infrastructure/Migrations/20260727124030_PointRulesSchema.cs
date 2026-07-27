using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <summary>
    /// Э0 схемы правил очков (docs/points-rules-per-competition-plan.md):
    /// 1) ClubPointsRules/ClubPointsRuleEntries → PointRulesClubs/PointRulesClubsEntries —
    ///    именно RENAME, а не DROP+CREATE (EF по умолчанию скаффолдит второе и теряет данные:
    ///    сид пересоздался бы через InsertData, но правила, заведённые вручную, исчезли бы);
    /// 2) новые PointRulesSwimmers/PointRulesSwimmersEntries (правила очков пловца);
    /// 3) Competition += PointRuleClubsId / PointRuleSwimmersId (ON DELETE RESTRICT).
    /// Поведение расчёта не меняется — FK пустые, никто их пока не читает.
    /// </summary>
    public partial class PointRulesSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── 1. Переименование клубной пары (данные сохраняются) ──────────────────
            migrationBuilder.Sql(@"
                ALTER TABLE ""ClubPointsRules""       RENAME TO ""PointRulesClubs"";
                ALTER TABLE ""ClubPointsRuleEntries"" RENAME TO ""PointRulesClubsEntries"";

                ALTER INDEX ""PK_ClubPointsRules""       RENAME TO ""PK_PointRulesClubs"";
                ALTER INDEX ""PK_ClubPointsRuleEntries"" RENAME TO ""PK_PointRulesClubsEntries"";
                ALTER INDEX ""IX_ClubPointsRules_Version""
                    RENAME TO ""IX_PointRulesClubs_Version"";
                ALTER INDEX ""IX_ClubPointsRuleEntries_RuleId_Place""
                    RENAME TO ""IX_PointRulesClubsEntries_RuleId_Place"";

                ALTER TABLE ""PointRulesClubsEntries""
                    RENAME CONSTRAINT ""FK_ClubPointsRuleEntries_ClubPointsRules_RuleId""
                    TO ""FK_PointRulesClubsEntries_PointRulesClubs_RuleId"";
            ");

            // ── 2. Привязка правил к соревнованию ────────────────────────────────────
            migrationBuilder.AddColumn<int>(
                name: "PointRuleClubsId",
                table: "Competitions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PointRuleSwimmersId",
                table: "Competitions",
                type: "integer",
                nullable: true);

            // ── 3. Правила очков пловца ─────────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "PointRulesSwimmers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    Description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Scope = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PointsSource = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DefaultPoints = table.Column<int>(type: "integer", nullable: false),
                    MaxScoringPlace = table.Column<int>(type: "integer", nullable: true),
                    CountBestSwims = table.Column<int>(type: "integer", nullable: true),
                    GroupBy = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SplitByGender = table.Column<bool>(type: "boolean", nullable: false),
                    IncludeRelays = table.Column<bool>(type: "boolean", nullable: false),
                    MinSwims = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PointRulesSwimmers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PointRulesSwimmersEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RuleId = table.Column<int>(type: "integer", nullable: false),
                    Place = table.Column<int>(type: "integer", nullable: false),
                    Points = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PointRulesSwimmersEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PointRulesSwimmersEntries_PointRulesSwimmers_RuleId",
                        column: x => x.RuleId,
                        principalTable: "PointRulesSwimmers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Competitions_PointRuleClubsId",
                table: "Competitions",
                column: "PointRuleClubsId");

            migrationBuilder.CreateIndex(
                name: "IX_Competitions_PointRuleSwimmersId",
                table: "Competitions",
                column: "PointRuleSwimmersId");

            migrationBuilder.CreateIndex(
                name: "IX_PointRulesSwimmers_Version",
                table: "PointRulesSwimmers",
                column: "Version",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PointRulesSwimmersEntries_RuleId_Place",
                table: "PointRulesSwimmersEntries",
                columns: new[] { "RuleId", "Place" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Competitions_PointRulesClubs_PointRuleClubsId",
                table: "Competitions",
                column: "PointRuleClubsId",
                principalTable: "PointRulesClubs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Competitions_PointRulesSwimmers_PointRuleSwimmersId",
                table: "Competitions",
                column: "PointRuleSwimmersId",
                principalTable: "PointRulesSwimmers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // ── 4. Гранты публичному read-пути (fail-closed, см. server/db/setup-roles.sql).
            // Роли может не быть (CI / чистая машина) — тогда молча пропускаем.
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF EXISTS (SELECT FROM pg_roles WHERE rolname = 'swimm_ro') THEN
                        GRANT SELECT ON
                            ""PointRulesClubs"", ""PointRulesClubsEntries"",
                            ""PointRulesSwimmers"", ""PointRulesSwimmersEntries""
                        TO swimm_ro;
                    END IF;
                END$$;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Competitions_PointRulesClubs_PointRuleClubsId",
                table: "Competitions");

            migrationBuilder.DropForeignKey(
                name: "FK_Competitions_PointRulesSwimmers_PointRuleSwimmersId",
                table: "Competitions");

            migrationBuilder.DropTable(
                name: "PointRulesSwimmersEntries");

            migrationBuilder.DropTable(
                name: "PointRulesSwimmers");

            migrationBuilder.DropIndex(
                name: "IX_Competitions_PointRuleClubsId",
                table: "Competitions");

            migrationBuilder.DropIndex(
                name: "IX_Competitions_PointRuleSwimmersId",
                table: "Competitions");

            migrationBuilder.DropColumn(
                name: "PointRuleClubsId",
                table: "Competitions");

            migrationBuilder.DropColumn(
                name: "PointRuleSwimmersId",
                table: "Competitions");

            // Обратное переименование — данные так же сохраняются.
            migrationBuilder.Sql(@"
                ALTER TABLE ""PointRulesClubsEntries""
                    RENAME CONSTRAINT ""FK_PointRulesClubsEntries_PointRulesClubs_RuleId""
                    TO ""FK_ClubPointsRuleEntries_ClubPointsRules_RuleId"";

                ALTER INDEX ""IX_PointRulesClubsEntries_RuleId_Place""
                    RENAME TO ""IX_ClubPointsRuleEntries_RuleId_Place"";
                ALTER INDEX ""IX_PointRulesClubs_Version""
                    RENAME TO ""IX_ClubPointsRules_Version"";
                ALTER INDEX ""PK_PointRulesClubsEntries"" RENAME TO ""PK_ClubPointsRuleEntries"";
                ALTER INDEX ""PK_PointRulesClubs""        RENAME TO ""PK_ClubPointsRules"";

                ALTER TABLE ""PointRulesClubsEntries"" RENAME TO ""ClubPointsRuleEntries"";
                ALTER TABLE ""PointRulesClubs""        RENAME TO ""ClubPointsRules"";
            ");
        }
    }
}
