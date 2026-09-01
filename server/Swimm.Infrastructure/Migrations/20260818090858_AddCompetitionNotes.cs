using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCompetitionNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CompetitionNotes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompetitionId = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ScaleDiffJson = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompetitionNotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CompetitionNotes_Competitions_CompetitionId",
                        column: x => x.CompetitionId,
                        principalTable: "Competitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CompetitionNoteTexts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    NoteId = table.Column<int>(type: "integer", nullable: false),
                    Lang = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    Body = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompetitionNoteTexts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CompetitionNoteTexts_CompetitionNotes_NoteId",
                        column: x => x.NoteId,
                        principalTable: "CompetitionNotes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CompetitionNotes_CompetitionId_Kind",
                table: "CompetitionNotes",
                columns: new[] { "CompetitionId", "Kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompetitionNoteTexts_NoteId_Lang",
                table: "CompetitionNoteTexts",
                columns: new[] { "NoteId", "Lang" },
                unique: true);

            // Примечание читает публичная витрина (попап «Points system»), а не только админка,
            // поэтому таблицы БЕЗ префикса Sys_ и нужен явный grant роли публичного чтения.
            // Роль swimm_ro может отсутствовать (чистая БД, CI, дев без setup-roles):
            // тогда грант молча пропускаем, иначе `--migrate` падает на пустой базе.
            // Базовый набор грантов — server/db/02-grants.sql.
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF EXISTS (SELECT FROM pg_roles WHERE rolname = 'swimm_ro') THEN
                        GRANT SELECT ON ""CompetitionNotes"", ""CompetitionNoteTexts"" TO swimm_ro;
                    END IF;
                END$$;
            ");

            // Переезд объяснения, которое успели написать колонкой Competitions.ClubPointsVerifiedNote
            // (английский текст лета-2025): сначала переносим, потом роняем колонку — держать два
            // места для одного текста хуже, чем любое из них.
            migrationBuilder.Sql(@"
                INSERT INTO ""CompetitionNotes"" (""CompetitionId"", ""Kind"", ""ScaleDiffJson"", ""UpdatedAt"", ""UpdatedBy"")
                SELECT ""Id"", 'club-points-mismatch', NULL, now() AT TIME ZONE 'utc', 'migration'
                FROM ""Competitions""
                WHERE ""ClubPointsVerifiedNote"" IS NOT NULL;

                INSERT INTO ""CompetitionNoteTexts"" (""NoteId"", ""Lang"", ""Body"")
                SELECT n.""Id"", 'en', c.""ClubPointsVerifiedNote""
                FROM ""CompetitionNotes"" n
                JOIN ""Competitions"" c ON c.""Id"" = n.""CompetitionId""
                WHERE n.""Kind"" = 'club-points-mismatch' AND c.""ClubPointsVerifiedNote"" IS NOT NULL;
                ");

            migrationBuilder.DropColumn(
                name: "ClubPointsVerifiedNote",
                table: "Competitions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ClubPointsVerifiedNote",
                table: "Competitions",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            // Английский текст возвращаем в колонку, иначе откат потерял бы объяснение.
            migrationBuilder.Sql(@"
                UPDATE ""Competitions"" c
                SET ""ClubPointsVerifiedNote"" = left(t.""Body"", 2000)
                FROM ""CompetitionNotes"" n
                JOIN ""CompetitionNoteTexts"" t ON t.""NoteId"" = n.""Id"" AND t.""Lang"" = 'en'
                WHERE n.""CompetitionId"" = c.""Id"" AND n.""Kind"" = 'club-points-mismatch';
                ");

            migrationBuilder.DropTable(
                name: "CompetitionNoteTexts");

            migrationBuilder.DropTable(
                name: "CompetitionNotes");
        }
    }
}
