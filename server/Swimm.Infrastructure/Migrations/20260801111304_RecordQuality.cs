using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RecordQuality : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Sys_RecordIssues",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RegionType = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    RegionCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Category = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AgeKey = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Gender = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    PoolType = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Style = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Distance = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    FlaggedTime = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Reason = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Note = table.Column<string>(type: "text", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sys_RecordIssues", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Sys_RecordVerifications",
                columns: table => new
                {
                    RecordId = table.Column<int>(type: "integer", nullable: false),
                    Found = table.Column<bool>(type: "boolean", nullable: false),
                    ResultId = table.Column<long>(type: "bigint", nullable: true),
                    SwimmerId = table.Column<int>(type: "integer", nullable: true),
                    DateMatched = table.Column<bool>(type: "boolean", nullable: true),
                    CheckedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sys_RecordVerifications", x => x.RecordId);
                    table.ForeignKey(
                        name: "FK_Sys_RecordVerifications_Records_RecordId",
                        column: x => x.RecordId,
                        principalTable: "Records",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Sys_RecordIssues_RegionType_RegionCode_Category_AgeKey_Gend~",
                table: "Sys_RecordIssues",
                columns: new[] { "RegionType", "RegionCode", "Category", "AgeKey", "Gender", "PoolType", "Style", "Distance", "FlaggedTime" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sys_RecordIssues_Status",
                table: "Sys_RecordIssues",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Sys_RecordVerifications_Found",
                table: "Sys_RecordVerifications",
                column: "Found");

            // RQ-1 — первая спорная запись, найдена вручную 2026-08-01.
            // Полный разбор с доказательствами: docs/plans/records-quality-plan.md §6.
            // Заводится миграцией, чтобы реестр не начинался с пустоты на новой машине.
            migrationBuilder.InsertData(
                table: "Sys_RecordIssues",
                columns: new[]
                {
                    "RegionType", "RegionCode", "Category", "AgeKey", "Gender", "PoolType",
                    "Style", "Distance", "FlaggedTime", "Reason", "Status", "Note",
                    "CreatedBy", "CreatedAt", "UpdatedAt"
                },
                values: new object[]
                {
                    "country", "ISR", "age", "10", "female", "50m",
                    "backstroke", "50m", "34.08", "lcm-faster-than-scm", "open",
                    "RQ-1. 50 м спина, 50 м бассейн, ступень 10 (и перенос на 11), " +
                    "מירה מירוסלבה אושקובה, הפועל דולפין נתניה, 20/07/2025. " +
                    "1) На чемпионате 20-21/07/2025 полтинники плыли только 9-летние — у её " +
                    "возраста этой дистанции не было (проверено по Results и по протокольному JSON). " +
                    "2) Её же 100 спина в тот день — 1:35.78, 50 батт — 42.74. " +
                    "3) Во всём соревновании нет ни одного результата 33.5-34.6. " +
                    "4) Тот же рекорд в 25 м бассейне — 35.64 (1995), то есть длинная вода " +
                    "быстрее короткой, что невозможно. Гипотеза (не подтверждена): перестановка " +
                    "цифр 43.08 -> 34.08. Ошибка не наша: она уже есть в выгрузке от 28/12/2025.",
                    "vlad",
                    new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
                    new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc)
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Sys_RecordIssues");

            migrationBuilder.DropTable(
                name: "Sys_RecordVerifications");
        }
    }
}
