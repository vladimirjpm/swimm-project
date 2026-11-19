using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <summary>
    /// Справочник «место проведения → длина бассейна» (П4, docs/plans/prod-auto-pull-plan.md §4.4, Р-3):
    /// Sys_VenuePools + написания Sys_VenuePoolAliases. Пустой на старте: первичный список
    /// подтверждает Влад таблицей, наполняют его отдельным SQL на проде.
    /// </summary>
    public partial class AddVenuePools : Migration
    {
        /// <summary>
        /// Обе таблицы в <c>server/db/seed-tables.txt</c>: справочник правит Влад на проде, локальная
        /// копия получает его синхронизацией. Триггер версии — здесь; сверяет DataVersionPgTests.
        /// Гранта swimm_ro нет (служебные, витрина не читает).
        /// </summary>
        internal static readonly string[] SyncTables = ["Sys_VenuePoolAliases", "Sys_VenuePools"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Sys_VenuePools",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    PoolType = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    IsAmbiguous = table.Column<bool>(type: "boolean", nullable: false),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ConfirmedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ConfirmedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sys_VenuePools", x => x.Id);
                    table.CheckConstraint("CK_Sys_VenuePools_PoolType", "(\"IsAmbiguous\" AND \"PoolType\" IS NULL) OR (NOT \"IsAmbiguous\" AND \"PoolType\" IS NOT NULL AND \"PoolType\" IN ('25m', '50m'))");
                });

            migrationBuilder.CreateTable(
                name: "Sys_VenuePoolAliases",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    VenuePoolId = table.Column<int>(type: "integer", nullable: false),
                    Alias = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    NormalizedAlias = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sys_VenuePoolAliases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Sys_VenuePoolAliases_Sys_VenuePools_VenuePoolId",
                        column: x => x.VenuePoolId,
                        principalTable: "Sys_VenuePools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Sys_VenuePoolAliases_NormalizedAlias",
                table: "Sys_VenuePoolAliases",
                column: "NormalizedAlias",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sys_VenuePoolAliases_VenuePoolId",
                table: "Sys_VenuePoolAliases",
                column: "VenuePoolId");

            migrationBuilder.CreateIndex(
                name: "IX_Sys_VenuePools_Name",
                table: "Sys_VenuePools",
                column: "Name",
                unique: true);

            // Грант swimm_sync: таблицы едут в синхронизацию «прод → локально» (seed-tables.txt).
            // На проде после --migrate 02-grants.sql никто не перезапускает — без этого
            // /db-sync-from-prod упал бы на новых таблицах. Роли может не быть (чистая БД, CI).
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF EXISTS (SELECT FROM pg_roles WHERE rolname = 'swimm_sync') THEN
                        GRANT SELECT ON ""Sys_VenuePools"", ""Sys_VenuePoolAliases"" TO swimm_sync;
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

            migrationBuilder.DropTable(
                name: "Sys_VenuePoolAliases");

            migrationBuilder.DropTable(
                name: "Sys_VenuePools");
        }
    }
}
