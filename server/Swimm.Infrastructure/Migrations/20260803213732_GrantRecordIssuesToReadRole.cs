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
            // Роль swimm_ro может отсутствовать (чистая БД, CI, дев без setup-roles):
            // тогда грант молча пропускаем, иначе `--migrate` падает на пустой базе.
            // Базовый набор грантов — server/db/02-grants.sql.
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF EXISTS (SELECT FROM pg_roles WHERE rolname = 'swimm_ro') THEN
                        GRANT SELECT ON ""Sys_RecordIssues"" TO swimm_ro;
                    END IF;
                END$$;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("REVOKE SELECT ON \"Sys_RecordIssues\" FROM swimm_ro;");
        }
    }
}
