using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swimm.API.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Clubs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Clubs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Competitions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Country = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Date = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PoolType = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false),
                    IsMasters = table.Column<bool>(type: "bit", nullable: false),
                    IsAward = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Competitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EventStyles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Distance = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Gender = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventStyles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Relays",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TeamName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SwimmersName = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Relays", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Swimmers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LastName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    FirstName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    LastNameEn = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    FirstNameEn = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    BirthYear = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Swimmers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Results",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompetitionId = table.Column<int>(type: "int", nullable: false),
                    EventStyleId = table.Column<int>(type: "int", nullable: false),
                    SwimmerId = table.Column<int>(type: "int", nullable: false),
                    ClubId = table.Column<int>(type: "int", nullable: false),
                    RelayId = table.Column<int>(type: "int", nullable: true),
                    AgeGroup = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    EventStyleAge = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Event = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Position = table.Column<int>(type: "int", nullable: true),
                    PositionAgeGroup = table.Column<int>(type: "int", nullable: true),
                    Heat = table.Column<int>(type: "int", nullable: false),
                    Lane = table.Column<int>(type: "int", nullable: false),
                    Time = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    TimeMillisecond = table.Column<int>(type: "int", nullable: true),
                    TimeSplit = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    TimeFail = table.Column<bool>(type: "bit", nullable: false),
                    TimeFailNote = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    InternationalPoints = table.Column<int>(type: "int", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Results", x => x.Id);
                    table.CheckConstraint("CK_Results_Heat_NonNegative", "[Heat] >= 0");
                    table.CheckConstraint("CK_Results_InternationalPoints_NonNegative", "[InternationalPoints] >= 0");
                    table.CheckConstraint("CK_Results_Lane_NonNegative", "[Lane] >= 0");
                    table.CheckConstraint("CK_Results_Position_PositiveOrNull", "[Position] IS NULL OR [Position] > 0");
                    table.CheckConstraint("CK_Results_PositionAgeGroup_PositiveOrNull", "[PositionAgeGroup] IS NULL OR [PositionAgeGroup] > 0");
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
                        name: "FK_Results_EventStyles_EventStyleId",
                        column: x => x.EventStyleId,
                        principalTable: "EventStyles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Results_Relays_RelayId",
                        column: x => x.RelayId,
                        principalTable: "Relays",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Results_Swimmers_SwimmerId",
                        column: x => x.SwimmerId,
                        principalTable: "Swimmers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

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
                name: "IX_EventStyles_Name_Distance_Gender",
                table: "EventStyles",
                columns: new[] { "Name", "Distance", "Gender" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Results_ClubId",
                table: "Results",
                column: "ClubId");

            migrationBuilder.CreateIndex(
                name: "IX_Results_CompetitionId",
                table: "Results",
                column: "CompetitionId");

            migrationBuilder.CreateIndex(
                name: "IX_Results_CompetitionId_EventStyleId_EventStyleAge",
                table: "Results",
                columns: new[] { "CompetitionId", "EventStyleId", "EventStyleAge" });

            migrationBuilder.CreateIndex(
                name: "IX_Results_CompetitionId_Position",
                table: "Results",
                columns: new[] { "CompetitionId", "Position" });

            migrationBuilder.CreateIndex(
                name: "IX_Results_CompetitionId_TimeMillisecond",
                table: "Results",
                columns: new[] { "CompetitionId", "TimeMillisecond" });

            migrationBuilder.CreateIndex(
                name: "IX_Results_EventStyleId",
                table: "Results",
                column: "EventStyleId");

            migrationBuilder.CreateIndex(
                name: "IX_Results_RelayId",
                table: "Results",
                column: "RelayId");

            migrationBuilder.CreateIndex(
                name: "IX_Results_SwimmerId",
                table: "Results",
                column: "SwimmerId");

            migrationBuilder.CreateIndex(
                name: "IX_Swimmers_LastName_FirstName",
                table: "Swimmers",
                columns: new[] { "LastName", "FirstName" });

            migrationBuilder.CreateIndex(
                name: "IX_Swimmers_LastNameEn_FirstNameEn",
                table: "Swimmers",
                columns: new[] { "LastNameEn", "FirstNameEn" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Results");

            migrationBuilder.DropTable(
                name: "Clubs");

            migrationBuilder.DropTable(
                name: "Competitions");

            migrationBuilder.DropTable(
                name: "EventStyles");

            migrationBuilder.DropTable(
                name: "Relays");

            migrationBuilder.DropTable(
                name: "Swimmers");
        }
    }
}
