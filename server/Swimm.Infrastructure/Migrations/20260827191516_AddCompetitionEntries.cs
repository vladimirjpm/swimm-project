using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCompetitionEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CompetitionEntries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OrgCompId = table.Column<int>(type: "integer", nullable: false),
                    CompetitionId = table.Column<int>(type: "integer", nullable: true),
                    CompDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CompName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    SwimmerId = table.Column<int>(type: "integer", nullable: false),
                    ClubId = table.Column<int>(type: "integer", nullable: false),
                    StyleId = table.Column<int>(type: "integer", nullable: false),
                    Distance = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Gender = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    EventCategory = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    AgeBand = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    OrgEventNumber = table.Column<int>(type: "integer", nullable: true),
                    OrgDisciplineId = table.Column<int>(type: "integer", nullable: false),
                    Heat = table.Column<int>(type: "integer", nullable: false),
                    Lane = table.Column<int>(type: "integer", nullable: false),
                    HeatStartAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Round = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    SeedTimeMs = table.Column<int>(type: "integer", nullable: true),
                    SeedTimeOriginal = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ResultId = table.Column<long>(type: "bigint", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PulledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompetitionEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CompetitionEntries_Clubs_ClubId",
                        column: x => x.ClubId,
                        principalTable: "Clubs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompetitionEntries_Competitions_CompetitionId",
                        column: x => x.CompetitionId,
                        principalTable: "Competitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_CompetitionEntries_Results_ResultId",
                        column: x => x.ResultId,
                        principalTable: "Results",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_CompetitionEntries_Styles_StyleId",
                        column: x => x.StyleId,
                        principalTable: "Styles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompetitionEntries_Swimmers_SwimmerId",
                        column: x => x.SwimmerId,
                        principalTable: "Swimmers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Sys_StartListPulls",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OrgCompId = table.Column<int>(type: "integer", nullable: false),
                    PulledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Events = table.Column<int>(type: "integer", nullable: false),
                    Entries = table.Column<int>(type: "integer", nullable: false),
                    Added = table.Column<int>(type: "integer", nullable: false),
                    Removed = table.Column<int>(type: "integer", nullable: false),
                    Moved = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sys_StartListPulls", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CompetitionEntries_ClubId_OrgCompId",
                table: "CompetitionEntries",
                columns: new[] { "ClubId", "OrgCompId" });

            migrationBuilder.CreateIndex(
                name: "IX_CompetitionEntries_CompetitionId",
                table: "CompetitionEntries",
                column: "CompetitionId");

            migrationBuilder.CreateIndex(
                name: "IX_CompetitionEntries_OrgCompId_HeatStartAt",
                table: "CompetitionEntries",
                columns: new[] { "OrgCompId", "HeatStartAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CompetitionEntries_OrgDisciplineId_Heat_Lane_SwimmerId",
                table: "CompetitionEntries",
                columns: new[] { "OrgDisciplineId", "Heat", "Lane", "SwimmerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompetitionEntries_ResultId",
                table: "CompetitionEntries",
                column: "ResultId");

            migrationBuilder.CreateIndex(
                name: "IX_CompetitionEntries_StyleId",
                table: "CompetitionEntries",
                column: "StyleId");

            migrationBuilder.CreateIndex(
                name: "IX_CompetitionEntries_SwimmerId_HeatStartAt",
                table: "CompetitionEntries",
                columns: new[] { "SwimmerId", "HeatStartAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Sys_StartListPulls_OrgCompId_PulledAt",
                table: "Sys_StartListPulls",
                columns: new[] { "OrgCompId", "PulledAt" });

            // Грант swimm_ro — CompetitionEntries читается анонимным public read-path
            // (стартовый протокол публичен так же, как результаты). Sys_StartListPulls
            // гранта НЕ получает: журнал заборов — внутренняя кухня.
            // Роль swimm_ro может отсутствовать (чистая БД, CI, дев без setup-roles):
            // тогда грант молча пропускаем, иначе `--migrate` падает на пустой базе.
            // Базовый набор грантов — server/db/02-grants.sql.
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF EXISTS (SELECT FROM pg_roles WHERE rolname = 'swimm_ro') THEN
                        GRANT SELECT ON ""CompetitionEntries"" TO swimm_ro;
                    END IF;
                END$$;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CompetitionEntries");

            migrationBuilder.DropTable(
                name: "Sys_StartListPulls");
        }
    }
}
