using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddHubGroupMedia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Sys_HubGroupMedia",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    HubGroupId = table.Column<int>(type: "integer", nullable: false),
                    TrainingId = table.Column<int>(type: "integer", nullable: true),
                    MediaType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SourceType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Caption = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sys_HubGroupMedia", x => x.Id);
                    table.CheckConstraint("CK_HubGroupMedia_AlbumInvariant", "(\"MediaType\" = 'album') = (\"SourceType\" = 'album')");
                    table.CheckConstraint("CK_HubGroupMedia_MediaType", "\"MediaType\" IN ('image', 'video', 'album')");
                    table.CheckConstraint("CK_HubGroupMedia_SourceType", "\"SourceType\" IN ('youtube', 'vimeo', 'album', 'other')");
                    table.ForeignKey(
                        name: "FK_Sys_HubGroupMedia_HubGroups_HubGroupId",
                        column: x => x.HubGroupId,
                        principalTable: "HubGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Sys_HubGroupMedia_Sys_AppUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Sys_AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Sys_HubGroupMedia_Sys_TrainingSessions_TrainingId",
                        column: x => x.TrainingId,
                        principalTable: "Sys_TrainingSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Sys_HubGroupMedia_CreatedByUserId",
                table: "Sys_HubGroupMedia",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Sys_HubGroupMedia_HubGroupId",
                table: "Sys_HubGroupMedia",
                column: "HubGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_Sys_HubGroupMedia_TrainingId",
                table: "Sys_HubGroupMedia",
                column: "TrainingId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Sys_HubGroupMedia");
        }
    }
}
