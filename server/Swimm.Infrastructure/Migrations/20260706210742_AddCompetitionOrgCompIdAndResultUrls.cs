using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCompetitionOrgCompIdAndResultUrls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "OrgCompId",
                table: "Competitions",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CompetitionResultUrls",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OrgCompId = table.Column<int>(type: "integer", nullable: false),
                    Culture = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompetitionResultUrls", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CompetitionResultUrls_OrgCompId_Culture",
                table: "CompetitionResultUrls",
                columns: new[] { "OrgCompId", "Culture" },
                unique: true);

            // Реальный UNIQUE constraint на nullable-колонке (Postgres допускает несколько NULL) —
            // не смоделирован в EF (alternate key потребовал бы NOT NULL), нужен для FK ниже.
            migrationBuilder.Sql(
                "ALTER TABLE \"Competitions\" ADD CONSTRAINT \"AK_Competitions_OrgCompId\" UNIQUE (\"OrgCompId\");");

            migrationBuilder.Sql(
                "ALTER TABLE \"CompetitionResultUrls\" ADD CONSTRAINT \"FK_CompetitionResultUrls_Competitions_OrgCompId\" " +
                "FOREIGN KEY (\"OrgCompId\") REFERENCES \"Competitions\" (\"OrgCompId\") ON DELETE CASCADE;");

            migrationBuilder.Sql("GRANT SELECT ON \"CompetitionResultUrls\" TO swimm_ro;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE \"CompetitionResultUrls\" DROP CONSTRAINT IF EXISTS \"FK_CompetitionResultUrls_Competitions_OrgCompId\";");

            migrationBuilder.Sql(
                "ALTER TABLE \"Competitions\" DROP CONSTRAINT IF EXISTS \"AK_Competitions_OrgCompId\";");

            migrationBuilder.DropTable(
                name: "CompetitionResultUrls");

            migrationBuilder.DropColumn(
                name: "OrgCompId",
                table: "Competitions");
        }
    }
}
