using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Swimm.API.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Competitions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Country = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Date = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PoolType = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    IsMasters = table.Column<bool>(type: "boolean", nullable: false),
                    IsAward = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Competitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Countries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CountryCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    CountryName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Countries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Galleries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Galleries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Relays",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TeamName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    SwimmersName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Relays", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Styles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Styles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Sys_AppRoles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sys_AppRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Sys_ImportHistory",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompetitionId = table.Column<int>(type: "integer", nullable: false),
                    ImportFileName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ImportDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Approved = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sys_ImportHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Sys_ImportHistory_Competitions_CompetitionId",
                        column: x => x.CompetitionId,
                        principalTable: "Competitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Clubs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CountryId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Clubs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Clubs_Countries_CountryId",
                        column: x => x.CountryId,
                        principalTable: "Countries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "GalleryItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GalleryId = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    SourceType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Code = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GalleryItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GalleryItems_Galleries_GalleryId",
                        column: x => x.GalleryId,
                        principalTable: "Galleries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Swimmers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    LastName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    FirstName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    LastNameEn = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    FirstNameEn = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    BirthYear = table.Column<int>(type: "integer", nullable: false),
                    Gender = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    SwimmerOrgId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    AvatarUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ClubId = table.Column<int>(type: "integer", nullable: true),
                    CountryId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Swimmers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Swimmers_Clubs_ClubId",
                        column: x => x.ClubId,
                        principalTable: "Clubs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Swimmers_Countries_CountryId",
                        column: x => x.CountryId,
                        principalTable: "Countries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Results",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompetitionId = table.Column<int>(type: "integer", nullable: false),
                    SwimmerId = table.Column<int>(type: "integer", nullable: false),
                    ClubId = table.Column<int>(type: "integer", nullable: false),
                    StyleId = table.Column<int>(type: "integer", nullable: false),
                    RelayId = table.Column<int>(type: "integer", nullable: true),
                    GalleryId = table.Column<int>(type: "integer", nullable: true),
                    CountryId = table.Column<int>(type: "integer", nullable: true),
                    CompetitionDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    Distance = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Gender = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    AgeGroup = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    EventStyleAge = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: true),
                    PositionAgeGroup = table.Column<int>(type: "integer", nullable: true),
                    Heat = table.Column<int>(type: "integer", nullable: false),
                    Lane = table.Column<int>(type: "integer", nullable: false),
                    TimeMillisecond = table.Column<int>(type: "integer", nullable: true),
                    TimeOriginal = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    TimeSplit = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    TimeFail = table.Column<bool>(type: "boolean", nullable: false),
                    TimeFailNote = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    InternationalPoints = table.Column<int>(type: "integer", nullable: false),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Results", x => x.Id);
                    table.CheckConstraint("CK_Results_Heat_NonNegative", "\"Heat\" >= 0");
                    table.CheckConstraint("CK_Results_InternationalPoints_NonNegative", "\"InternationalPoints\" >= 0");
                    table.CheckConstraint("CK_Results_Lane_NonNegative", "\"Lane\" >= 0");
                    table.CheckConstraint("CK_Results_Position_PositiveOrNull", "\"Position\" IS NULL OR \"Position\" > 0");
                    table.CheckConstraint("CK_Results_PositionAgeGroup_PositiveOrNull", "\"PositionAgeGroup\" IS NULL OR \"PositionAgeGroup\" > 0");
                    table.ForeignKey(
                        name: "FK_Results_Clubs_ClubId",
                        column: x => x.ClubId,
                        principalTable: "Clubs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Results_Competitions_CompetitionId",
                        column: x => x.CompetitionId,
                        principalTable: "Competitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Results_Countries_CountryId",
                        column: x => x.CountryId,
                        principalTable: "Countries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Results_Galleries_GalleryId",
                        column: x => x.GalleryId,
                        principalTable: "Galleries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Results_Relays_RelayId",
                        column: x => x.RelayId,
                        principalTable: "Relays",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Results_Styles_StyleId",
                        column: x => x.StyleId,
                        principalTable: "Styles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Results_Swimmers_SwimmerId",
                        column: x => x.SwimmerId,
                        principalTable: "Swimmers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Sys_AppUsers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AvatarUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    SwimmerId = table.Column<int>(type: "integer", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sys_AppUsers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Sys_AppUsers_Swimmers_SwimmerId",
                        column: x => x.SwimmerId,
                        principalTable: "Swimmers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Sys_AppUserRoles",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    RoleId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sys_AppUserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_Sys_AppUserRoles_Sys_AppRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Sys_AppRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Sys_AppUserRoles_Sys_AppUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "Sys_AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Sys_UserExternalLogins",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ProviderKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sys_UserExternalLogins", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Sys_UserExternalLogins_Sys_AppUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "Sys_AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Sys_UserLoginHistory",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    IpAddress = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    LoginAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sys_UserLoginHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Sys_UserLoginHistory_Sys_AppUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "Sys_AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "Styles",
                columns: new[] { "Id", "Name" },
                values: new object[,]
                {
                    { 1, "freestyle" },
                    { 2, "backstroke" },
                    { 3, "breaststroke" },
                    { 4, "butterfly" },
                    { 5, "individual_medley" },
                    { 6, "medley_relay" },
                    { 7, "free_relay" }
                });

            migrationBuilder.InsertData(
                table: "Sys_AppRoles",
                columns: new[] { "Id", "Name" },
                values: new object[,]
                {
                    { 1, "Admin" },
                    { 2, "User" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Clubs_CountryId",
                table: "Clubs",
                column: "CountryId");

            migrationBuilder.CreateIndex(
                name: "IX_Clubs_Name",
                table: "Clubs",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_Competitions_Name_Date_PoolType",
                table: "Competitions",
                columns: new[] { "Name", "Date", "PoolType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Countries_CountryCode",
                table: "Countries",
                column: "CountryCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GalleryItems_GalleryId",
                table: "GalleryItems",
                column: "GalleryId");

            migrationBuilder.CreateIndex(
                name: "IX_Results_ClubId",
                table: "Results",
                column: "ClubId");

            migrationBuilder.CreateIndex(
                name: "IX_Results_CompetitionDate",
                table: "Results",
                column: "CompetitionDate");

            migrationBuilder.CreateIndex(
                name: "IX_Results_CompetitionId",
                table: "Results",
                column: "CompetitionId");

            migrationBuilder.CreateIndex(
                name: "IX_Results_CompetitionId_StyleId_Distance_Gender",
                table: "Results",
                columns: new[] { "CompetitionId", "StyleId", "Distance", "Gender" });

            migrationBuilder.CreateIndex(
                name: "IX_Results_CompetitionId_TimeMillisecond",
                table: "Results",
                columns: new[] { "CompetitionId", "TimeMillisecond" });

            migrationBuilder.CreateIndex(
                name: "IX_Results_CountryId",
                table: "Results",
                column: "CountryId");

            migrationBuilder.CreateIndex(
                name: "IX_Results_GalleryId",
                table: "Results",
                column: "GalleryId");

            migrationBuilder.CreateIndex(
                name: "IX_Results_RelayId",
                table: "Results",
                column: "RelayId");

            migrationBuilder.CreateIndex(
                name: "IX_Results_StyleId_Distance",
                table: "Results",
                columns: new[] { "StyleId", "Distance" });

            migrationBuilder.CreateIndex(
                name: "IX_Results_SwimmerId",
                table: "Results",
                column: "SwimmerId");

            migrationBuilder.CreateIndex(
                name: "IX_Results_SwimmerId_TimeMillisecond",
                table: "Results",
                columns: new[] { "SwimmerId", "TimeMillisecond" });

            migrationBuilder.CreateIndex(
                name: "IX_Styles_Name",
                table: "Styles",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Swimmers_ClubId",
                table: "Swimmers",
                column: "ClubId");

            migrationBuilder.CreateIndex(
                name: "IX_Swimmers_CountryId",
                table: "Swimmers",
                column: "CountryId");

            migrationBuilder.CreateIndex(
                name: "IX_Swimmers_LastName_FirstName",
                table: "Swimmers",
                columns: new[] { "LastName", "FirstName" });

            migrationBuilder.CreateIndex(
                name: "IX_Swimmers_LastNameEn_FirstNameEn",
                table: "Swimmers",
                columns: new[] { "LastNameEn", "FirstNameEn" });

            migrationBuilder.CreateIndex(
                name: "IX_Sys_AppRoles_Name",
                table: "Sys_AppRoles",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sys_AppUserRoles_RoleId",
                table: "Sys_AppUserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_Sys_AppUsers_Email",
                table: "Sys_AppUsers",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sys_AppUsers_SwimmerId",
                table: "Sys_AppUsers",
                column: "SwimmerId");

            migrationBuilder.CreateIndex(
                name: "IX_Sys_ImportHistory_CompetitionId",
                table: "Sys_ImportHistory",
                column: "CompetitionId");

            migrationBuilder.CreateIndex(
                name: "IX_Sys_ImportHistory_ImportDate",
                table: "Sys_ImportHistory",
                column: "ImportDate");

            migrationBuilder.CreateIndex(
                name: "IX_Sys_UserExternalLogins_Provider_ProviderKey",
                table: "Sys_UserExternalLogins",
                columns: new[] { "Provider", "ProviderKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sys_UserExternalLogins_UserId",
                table: "Sys_UserExternalLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Sys_UserLoginHistory_LoginAt",
                table: "Sys_UserLoginHistory",
                column: "LoginAt");

            migrationBuilder.CreateIndex(
                name: "IX_Sys_UserLoginHistory_UserId",
                table: "Sys_UserLoginHistory",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GalleryItems");

            migrationBuilder.DropTable(
                name: "Results");

            migrationBuilder.DropTable(
                name: "Sys_AppUserRoles");

            migrationBuilder.DropTable(
                name: "Sys_ImportHistory");

            migrationBuilder.DropTable(
                name: "Sys_UserExternalLogins");

            migrationBuilder.DropTable(
                name: "Sys_UserLoginHistory");

            migrationBuilder.DropTable(
                name: "Galleries");

            migrationBuilder.DropTable(
                name: "Relays");

            migrationBuilder.DropTable(
                name: "Styles");

            migrationBuilder.DropTable(
                name: "Sys_AppRoles");

            migrationBuilder.DropTable(
                name: "Competitions");

            migrationBuilder.DropTable(
                name: "Sys_AppUsers");

            migrationBuilder.DropTable(
                name: "Swimmers");

            migrationBuilder.DropTable(
                name: "Clubs");

            migrationBuilder.DropTable(
                name: "Countries");
        }
    }
}
