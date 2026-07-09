using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRecordsAndNormativeStandards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NormativeStandards",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Kind = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Country = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Gender = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    PoolType = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    Style = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Distance = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    AgeKey = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Level = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: false),
                    Time = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NormativeStandards", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Records",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RegionType = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    RegionCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Category = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    AgeKey = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Gender = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    PoolType = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    Style = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Distance = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Time = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: false),
                    HolderName = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    Club = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    HolderCountry = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    RecordDate = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Records", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NormativeStandards_Kind_Country_Gender_PoolType_Style_Dista~",
                table: "NormativeStandards",
                columns: new[] { "Kind", "Country", "Gender", "PoolType", "Style", "Distance", "AgeKey", "Level" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Records_RegionType_RegionCode_Category",
                table: "Records",
                columns: new[] { "RegionType", "RegionCode", "Category" });

            migrationBuilder.CreateIndex(
                name: "IX_Records_RegionType_RegionCode_Category_AgeKey_Gender_PoolTy~",
                table: "Records",
                columns: new[] { "RegionType", "RegionCode", "Category", "AgeKey", "Gender", "PoolType", "Style", "Distance" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NormativeStandards");

            migrationBuilder.DropTable(
                name: "Records");
        }
    }
}
