using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <summary>
    /// pg_stat_statements — счётчик всех SQL самой базы (решение Влада 19.11.2026): сколько раз, сколько
    /// времени в сумме и в среднем, сколько читали с диска — по сайту, фоновым службам и ProdAdmin сразу.
    /// App Insights видит только медленные SQL сайта (быстрые не отправляются, Telemetry:SqlSpanMinMs).
    ///
    /// На Azure сбор включён параметрами сервера 19.11: библиотека в shared_preload_libraries по
    /// умолчанию, azure.extensions = PG_STAT_STATEMENTS, pg_stat_statements.track = top,
    /// track_utility = off (без BEGIN/COMMIT/SET). Эта миграция только создаёт представление в базе.
    /// Где расширения нет (другая сборка Postgres) — не падаем: статистика просто недоступна.
    ///
    /// pg_read_all_stats рабочей роли — чтобы `prod-sql.mjs read` видел ТЕКСТ запросов всех ролей:
    /// публичное чтение идёт ролью swimm_ro, без этого права её запросы были бы «insufficient privilege».
    /// Право только на чтение статистики, данных не открывает.
    /// </summary>
    public partial class AddPgStatStatements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    CREATE EXTENSION IF NOT EXISTS pg_stat_statements;
                EXCEPTION WHEN OTHERS THEN
                    RAISE NOTICE 'pg_stat_statements недоступен: %', SQLERRM;
                END$$;
            ");

            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF EXISTS (SELECT FROM pg_roles WHERE rolname = 'swimm_rw') THEN
                        GRANT pg_read_all_stats TO swimm_rw;
                    END IF;
                EXCEPTION WHEN insufficient_privilege THEN
                    RAISE NOTICE 'pg_read_all_stats не выдан: %', SQLERRM;
                END$$;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF EXISTS (SELECT FROM pg_roles WHERE rolname = 'swimm_rw') THEN
                        REVOKE pg_read_all_stats FROM swimm_rw;
                    END IF;
                END$$;
            ");
            migrationBuilder.Sql("DROP EXTENSION IF EXISTS pg_stat_statements;");
        }
    }
}
