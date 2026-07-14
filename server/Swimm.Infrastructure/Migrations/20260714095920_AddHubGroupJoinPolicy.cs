using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddHubGroupJoinPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "JoinPolicy",
                table: "HubGroups",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "open");

            migrationBuilder.AddCheckConstraint(
                name: "CK_HubGroups_JoinPolicy",
                table: "HubGroups",
                sql: "\"JoinPolicy\" IN ('open', 'approval')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_HubGroups_JoinPolicy",
                table: "HubGroups");

            migrationBuilder.DropColumn(
                name: "JoinPolicy",
                table: "HubGroups");
        }
    }
}
