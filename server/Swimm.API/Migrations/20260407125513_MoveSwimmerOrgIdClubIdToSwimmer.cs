using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swimm.API.Migrations
{
    /// <inheritdoc />
    public partial class MoveSwimmerOrgIdClubIdToSwimmer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AppUsers_Clubs_ClubId",
                table: "AppUsers");

            migrationBuilder.DropIndex(
                name: "IX_AppUsers_ClubId",
                table: "AppUsers");

            migrationBuilder.DropColumn(
                name: "ClubId",
                table: "AppUsers");

            migrationBuilder.DropColumn(
                name: "SwimmerOrgId",
                table: "AppUsers");

            migrationBuilder.AddColumn<string>(
                name: "AvatarUrl",
                table: "Swimmers",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ClubId",
                table: "Swimmers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Gender",
                table: "Swimmers",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SwimmerOrgId",
                table: "Swimmers",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Swimmers_ClubId",
                table: "Swimmers",
                column: "ClubId");

            migrationBuilder.AddForeignKey(
                name: "FK_Swimmers_Clubs_ClubId",
                table: "Swimmers",
                column: "ClubId",
                principalTable: "Clubs",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Swimmers_Clubs_ClubId",
                table: "Swimmers");

            migrationBuilder.DropIndex(
                name: "IX_Swimmers_ClubId",
                table: "Swimmers");

            migrationBuilder.DropColumn(
                name: "AvatarUrl",
                table: "Swimmers");

            migrationBuilder.DropColumn(
                name: "ClubId",
                table: "Swimmers");

            migrationBuilder.DropColumn(
                name: "Gender",
                table: "Swimmers");

            migrationBuilder.DropColumn(
                name: "SwimmerOrgId",
                table: "Swimmers");

            migrationBuilder.AddColumn<int>(
                name: "ClubId",
                table: "AppUsers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SwimmerOrgId",
                table: "AppUsers",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppUsers_ClubId",
                table: "AppUsers",
                column: "ClubId");

            migrationBuilder.AddForeignKey(
                name: "FK_AppUsers_Clubs_ClubId",
                table: "AppUsers",
                column: "ClubId",
                principalTable: "Clubs",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
