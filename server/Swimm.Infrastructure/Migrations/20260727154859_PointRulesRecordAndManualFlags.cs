using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PointRulesRecordAndManualFlags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "FinalsOnly",
                table: "PointRulesSwimmers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ManualOnly",
                table: "PointRulesSwimmers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "RecordPoints",
                table: "PointRulesSwimmers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RecordTiePoints",
                table: "PointRulesSwimmers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ManualOnly",
                table: "PointRulesClubs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // defaultValue: 2 (а не скаффолдовый 0) — существующий хардкод в ResultRepository
            // удваивает очки за эстафету всем правилам. С нулём правила, заведённые вручную
            // (не через HasData), молча начали бы давать за эстафеты 0.
            migrationBuilder.AddColumn<int>(
                name: "RelayMultiplier",
                table: "PointRulesClubs",
                type: "integer",
                nullable: false,
                defaultValue: 2);

            migrationBuilder.UpdateData(
                table: "PointRulesClubs",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "ManualOnly", "RelayMultiplier" },
                values: new object[] { false, 2 });

            migrationBuilder.UpdateData(
                table: "PointRulesClubs",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "ManualOnly", "RelayMultiplier" },
                values: new object[] { false, 2 });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FinalsOnly",
                table: "PointRulesSwimmers");

            migrationBuilder.DropColumn(
                name: "ManualOnly",
                table: "PointRulesSwimmers");

            migrationBuilder.DropColumn(
                name: "RecordPoints",
                table: "PointRulesSwimmers");

            migrationBuilder.DropColumn(
                name: "RecordTiePoints",
                table: "PointRulesSwimmers");

            migrationBuilder.DropColumn(
                name: "ManualOnly",
                table: "PointRulesClubs");

            migrationBuilder.DropColumn(
                name: "RelayMultiplier",
                table: "PointRulesClubs");
        }
    }
}
