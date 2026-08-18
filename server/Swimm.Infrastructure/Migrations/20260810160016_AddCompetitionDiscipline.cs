using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCompetitionDiscipline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Discipline",
                table: "Sys_DiscoveredCompetitions",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "swimming");

            migrationBuilder.AddColumn<string>(
                name: "Discipline",
                table: "Competitions",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "swimming");

            // Бэкфилл догадкой по названию — тем же правилом, что Disciplines.GuessFromName
            // (корень «מנותית» ловит обе ивритские формы: אומנותית и אמנותית).
            // Дефолт колонки — swimming, поэтому правим только артистическое.
            migrationBuilder.Sql(@"
                UPDATE ""Sys_DiscoveredCompetitions""
                   SET ""Discipline"" = 'artistic'
                 WHERE ""Name"" LIKE '%מנותית%'
                    OR lower(""Name"") LIKE '%artistic%'
                    OR lower(""Name"") LIKE '%synchro%';");

            migrationBuilder.Sql(@"
                UPDATE ""Competitions""
                   SET ""Discipline"" = 'artistic'
                 WHERE ""Name"" LIKE '%מנותית%'
                    OR lower(""Name"") LIKE '%artistic%'
                    OR lower(""Name"") LIKE '%synchro%';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Discipline",
                table: "Sys_DiscoveredCompetitions");

            migrationBuilder.DropColumn(
                name: "Discipline",
                table: "Competitions");
        }
    }
}
