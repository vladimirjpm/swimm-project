using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <summary>
    /// Замок прогона фоновой задачи (П1, docs/plans/prod-auto-pull-plan.md §4.1): заявка
    /// ClaimedAt / ClaimedBy в Sys_JobRuns. Прод и ProdAdmin смотрят в одну базу — заявку берёт
    /// один атомарный запрос (JobRunStore.TryClaimAsync), второй процесс выходит с «занято».
    /// Только две nullable-колонки: старый код их не замечает, миграцию можно накатить до деплоя.
    /// Таблица Sys_, без гранта swimm_ro — грантов не добавляем.
    /// </summary>
    public partial class AddJobRunClaim : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ClaimedAt",
                table: "Sys_JobRuns",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClaimedBy",
                table: "Sys_JobRuns",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ClaimedAt",
                table: "Sys_JobRuns");

            migrationBuilder.DropColumn(
                name: "ClaimedBy",
                table: "Sys_JobRuns");
        }
    }
}
