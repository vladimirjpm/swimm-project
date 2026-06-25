using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MoveSecurityStampToAppUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SecurityStamp",
                table: "Sys_UserLocalCredentials");

            // Каждой существующей строке — свой случайный штамп (md5(random()) → 32 hex, формат "N").
            // random() волатилен → Postgres вычисляет default по-строчно при rewrite.
            // Пустой штамп недопустим: валидатор куки reject'ит пустой claim → петля логина.
            migrationBuilder.AddColumn<string>(
                name: "SecurityStamp",
                table: "Sys_AppUsers",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValueSql: "md5(random()::text)");

            // Снимаем DB-default: дальше значение всегда задаёт приложение, а модель (snapshot)
            // default не описывает — иначе следующая миграция увидела бы дрейф.
            migrationBuilder.Sql(
                "ALTER TABLE \"Sys_AppUsers\" ALTER COLUMN \"SecurityStamp\" DROP DEFAULT;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SecurityStamp",
                table: "Sys_AppUsers");

            migrationBuilder.AddColumn<string>(
                name: "SecurityStamp",
                table: "Sys_UserLocalCredentials",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");
        }
    }
}
