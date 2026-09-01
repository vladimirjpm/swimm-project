using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRelayMembers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RelayMembers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RelayId = table.Column<int>(type: "integer", nullable: false),
                    SwimmerId = table.Column<int>(type: "integer", nullable: false),
                    LegOrder = table.Column<int>(type: "integer", nullable: false),
                    SplitTime = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RelayMembers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RelayMembers_Relays_RelayId",
                        column: x => x.RelayId,
                        principalTable: "Relays",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RelayMembers_Swimmers_SwimmerId",
                        column: x => x.SwimmerId,
                        principalTable: "Swimmers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RelayMembers_RelayId_SwimmerId",
                table: "RelayMembers",
                columns: new[] { "RelayId", "SwimmerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RelayMembers_SwimmerId",
                table: "RelayMembers",
                column: "SwimmerId");

            // Публичная reference-таблица (состав эстафет) — доступна анонимному read-пути.
            // Роль swimm_ro может отсутствовать (чистая БД, CI, дев без setup-roles):
            // тогда грант молча пропускаем, иначе `--migrate` падает на пустой базе.
            // Базовый набор грантов — server/db/02-grants.sql.
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF EXISTS (SELECT FROM pg_roles WHERE rolname = 'swimm_ro') THEN
                        GRANT SELECT ON ""RelayMembers"" TO swimm_ro;
                    END IF;
                END$$;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RelayMembers");
        }
    }
}
