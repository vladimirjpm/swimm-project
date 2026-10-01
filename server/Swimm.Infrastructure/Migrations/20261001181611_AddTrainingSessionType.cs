using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTrainingSessionType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Тип тренировки (решение Влада 01.10.2026): все уже залитые и заведённые — обычные
            // тренировки в воде, поэтому значение по умолчанию «swim», а не пустая строка.
            migrationBuilder.AddColumn<string>(
                name: "Type",
                table: "Sys_TrainingSessions",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "swim");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Type",
                table: "Sys_TrainingSessions");
        }
    }
}
