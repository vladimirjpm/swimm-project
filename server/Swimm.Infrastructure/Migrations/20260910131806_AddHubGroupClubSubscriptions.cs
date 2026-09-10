using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddHubGroupClubSubscriptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsExcluded",
                table: "HubGroupMembers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "HubGroupMembers",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "manual");

            migrationBuilder.CreateTable(
                name: "HubGroupClubSubscriptions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    HubGroupId = table.Column<int>(type: "integer", nullable: false),
                    ClubId = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HubGroupClubSubscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HubGroupClubSubscriptions_Clubs_ClubId",
                        column: x => x.ClubId,
                        principalTable: "Clubs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_HubGroupClubSubscriptions_HubGroups_HubGroupId",
                        column: x => x.HubGroupId,
                        principalTable: "HubGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_HubGroupMembers_ExcludedOnlyClub",
                table: "HubGroupMembers",
                sql: "NOT \"IsExcluded\" OR \"Source\" = 'club'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_HubGroupMembers_Source",
                table: "HubGroupMembers",
                sql: "\"Source\" IN ('manual', 'club')");

            migrationBuilder.CreateIndex(
                name: "IX_HubGroupClubSubscriptions_ClubId",
                table: "HubGroupClubSubscriptions",
                column: "ClubId");

            migrationBuilder.CreateIndex(
                name: "IX_HubGroupClubSubscriptions_HubGroupId",
                table: "HubGroupClubSubscriptions",
                column: "HubGroupId",
                unique: true);

            // Грант swimm_ro: подписки читает ПУБЛИЧНЫЙ путь (каталог групп прячет копии клуба
            // с официальной группой, страница группы пишет «Follows club X» — план П4).
            // Приватного в таблице нет: какой группа следит клуб, видно по её составу.
            // Роль может отсутствовать (чистая БД, CI, дев без setup-roles) — тогда грант
            // молча пропускаем, иначе `--migrate` падает на пустой базе. Базовый набор
            // грантов — server/db/02-grants.sql.
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF EXISTS (SELECT FROM pg_roles WHERE rolname = 'swimm_ro') THEN
                        GRANT SELECT ON ""HubGroupClubSubscriptions"" TO swimm_ro;
                    END IF;
                END$$;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HubGroupClubSubscriptions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_HubGroupMembers_ExcludedOnlyClub",
                table: "HubGroupMembers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_HubGroupMembers_Source",
                table: "HubGroupMembers");

            migrationBuilder.DropColumn(
                name: "IsExcluded",
                table: "HubGroupMembers");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "HubGroupMembers");
        }
    }
}
