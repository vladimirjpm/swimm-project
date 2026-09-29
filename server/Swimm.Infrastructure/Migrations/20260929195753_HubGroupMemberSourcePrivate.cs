using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HubGroupMemberSourcePrivate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_HubGroupMembers_ExcludedOnlyClub",
                table: "HubGroupMembers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_HubGroupMembers_Source",
                table: "HubGroupMembers");

            migrationBuilder.AddCheckConstraint(
                name: "CK_HubGroupMembers_ExcludedOnlyClub",
                table: "HubGroupMembers",
                sql: "NOT \"IsExcluded\" OR \"Source\" IN ('club', 'private')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_HubGroupMembers_Source",
                table: "HubGroupMembers",
                sql: "\"Source\" IN ('manual', 'club', 'private')");

            // Строки пловцов группы (Р71) — источник private: их можно сделать неактивными.
            migrationBuilder.Sql("""
                UPDATE "HubGroupMembers" m
                SET "Source" = 'private'
                FROM "Swimmers" s
                WHERE s."Id" = m."SwimmerId" AND s."PrivateHubGroupId" = m."HubGroupId";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_HubGroupMembers_ExcludedOnlyClub",
                table: "HubGroupMembers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_HubGroupMembers_Source",
                table: "HubGroupMembers");

            migrationBuilder.Sql("""
                UPDATE "HubGroupMembers" SET "Source" = 'manual', "IsExcluded" = false WHERE "Source" = 'private';
                """);

            migrationBuilder.AddCheckConstraint(
                name: "CK_HubGroupMembers_ExcludedOnlyClub",
                table: "HubGroupMembers",
                sql: "NOT \"IsExcluded\" OR \"Source\" = 'club'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_HubGroupMembers_Source",
                table: "HubGroupMembers",
                sql: "\"Source\" IN ('manual', 'club')");
        }
    }
}
