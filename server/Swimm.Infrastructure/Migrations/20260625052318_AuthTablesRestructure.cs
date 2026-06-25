using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AuthTablesRestructure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "Sys_UserExternalLogins",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "EmailVerified",
                table: "Sys_UserExternalLogins",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "Sys_UserExternalLogins",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Sys_ExternalLoginTokens",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ExternalLoginId = table.Column<int>(type: "integer", nullable: false),
                    AccessTokenProtected = table.Column<string>(type: "text", nullable: true),
                    RefreshTokenProtected = table.Column<string>(type: "text", nullable: true),
                    TokenType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Scopes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sys_ExternalLoginTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Sys_ExternalLoginTokens_Sys_UserExternalLogins_ExternalLogi~",
                        column: x => x.ExternalLoginId,
                        principalTable: "Sys_UserExternalLogins",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Sys_UserLocalCredentials",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    PasswordHash = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    PasswordAlgorithm = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    SecurityStamp = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EmailConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    EmailConfirmedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FailedLoginCount = table.Column<int>(type: "integer", nullable: false),
                    LockoutEnd = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sys_UserLocalCredentials", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_Sys_UserLocalCredentials_Sys_AppUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "Sys_AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Sys_ExternalLoginTokens_ExternalLoginId",
                table: "Sys_ExternalLoginTokens",
                column: "ExternalLoginId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Sys_ExternalLoginTokens");

            migrationBuilder.DropTable(
                name: "Sys_UserLocalCredentials");

            migrationBuilder.DropColumn(
                name: "Email",
                table: "Sys_UserExternalLogins");

            migrationBuilder.DropColumn(
                name: "EmailVerified",
                table: "Sys_UserExternalLogins");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Sys_UserExternalLogins");
        }
    }
}
