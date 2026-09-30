using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HubGroupPersonStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_HubGroupMembers_ExcludedOnlyClub",
                table: "HubGroupMembers");

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "Sys_HubGroupBreaks",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "break");

            migrationBuilder.AddCheckConstraint(
                name: "CK_HubGroupBreaks_Kind",
                table: "Sys_HubGroupBreaks",
                sql: "\"Kind\" IN ('break', 'not_training') AND (\"Kind\" = 'break' OR \"Until\" IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Без вида «не тренируется» строка читалась бы перерывом — закрываем такие строки;
            // ручного «Inactive» старое ограничение не допускает — возвращаем в состав.
            migrationBuilder.Sql(@"UPDATE ""Sys_HubGroupBreaks"" SET ""EndedAt"" = now() WHERE ""Kind"" = 'not_training' AND ""EndedAt"" IS NULL;");
            migrationBuilder.Sql(@"UPDATE ""HubGroupMembers"" SET ""IsExcluded"" = false WHERE ""Source"" = 'manual' AND ""IsExcluded"";");

            migrationBuilder.DropCheckConstraint(
                name: "CK_HubGroupBreaks_Kind",
                table: "Sys_HubGroupBreaks");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "Sys_HubGroupBreaks");

            migrationBuilder.AddCheckConstraint(
                name: "CK_HubGroupMembers_ExcludedOnlyClub",
                table: "HubGroupMembers",
                sql: "NOT \"IsExcluded\" OR \"Source\" IN ('club', 'private')");
        }
    }
}
