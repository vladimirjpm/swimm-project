using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRecordUpdatedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "Records",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            // Бэкфилл: у существующих строк UpdatedAt = DateTime.MinValue (значение колонки по
            // умолчанию) — подменяем на время миграции, чтобы не путать с «никогда не обновлялось».
            // Сравнение по году, а не точному timestamptz: default DateTime(1,1,1,..) c Kind=Unspecified
            // при записи в timestamptz сдвигается на смещение сессии/сервера, и точное '0001-01-01T00:00:00Z'
            // может не совпасть.
            migrationBuilder.Sql(
                "UPDATE \"Records\" SET \"UpdatedAt\" = now() WHERE EXTRACT(YEAR FROM \"UpdatedAt\") <= 1;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Records");
        }
    }
}
