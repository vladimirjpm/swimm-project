using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <summary>
    /// Инцидент И-18 (docs/data-integrity.md): у снятых пловцов (DQ / NS / DNS) в базе стояло
    /// «место». Числом в первой колонке протокола место у них не является — в одном заплыве
    /// оно повторяется у разных снятых, — а на витрине это выглядело как «#2 🥈 … DSQ».
    ///
    /// Схема не меняется: миграция чинит ДАННЫЕ. Запись правила живёт в `JsonImportService`
    /// (`Position = item.TimeFail ? null : item.Position`), здесь — разовая уборка того,
    /// что успело записаться до правила.
    /// </summary>
    public partial class DropPlaceForDisqualified : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE "Results"
                   SET "Position" = NULL,
                       "PositionAgeGroup" = NULL
                 WHERE "TimeFail" = true
                   AND ("Position" IS NOT NULL OR "PositionAgeGroup" IS NOT NULL);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Откатывать нечего и незачем: стёртые числа местами не были, восстановить их
            // можно только переимпортом протокола — и он же положит их уже по правилу.
        }
    }
}
