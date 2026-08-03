using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class GrantRecordIssuesToReadRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Единственная Sys_-таблица, открытая роли публичного чтения, и это осознанное
            // исключение. Sys_* закрыты от swimm_ro как внутренняя кухня, но реестр претензий
            // к рекордам — не кухня: это метка о ПУБЛИКУЕМОЙ записи справочника, и показывать
            // её надо ровно там, где показан сам рекорд (Record wall клуба, нормативы).
            // Альтернатива — гонять публичный путь через владельца БД — хуже: она размывает
            // границу ролей ради одного значка.
            migrationBuilder.Sql("GRANT SELECT ON \"Sys_RecordIssues\" TO swimm_ro;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("REVOKE SELECT ON \"Sys_RecordIssues\" FROM swimm_ro;");
        }
    }
}
