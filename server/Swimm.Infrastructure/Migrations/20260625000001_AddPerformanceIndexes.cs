using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPerformanceIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Cross-competition leaderboard: best times by style/distance/gender ordered by date
            migrationBuilder.Sql(
                @"CREATE INDEX IF NOT EXISTS ""IX_Results_StyleId_Distance_Gender_CompDate""
                  ON ""Results"" (""StyleId"", ""Distance"", ""Gender"", ""CompetitionDate"" DESC);");

            // varchar_pattern_ops indexes let PostgreSQL use B-tree for LIKE 'x%' (StartsWith)
            // without this, Hebrew/multi-byte names cause full seq-scan on Swimmers
            migrationBuilder.Sql(
                @"CREATE INDEX IF NOT EXISTS ""IX_Swimmers_LastName_pattern""
                  ON ""Swimmers"" (""LastName"" varchar_pattern_ops);");

            migrationBuilder.Sql(
                @"CREATE INDEX IF NOT EXISTS ""IX_Swimmers_FirstName_pattern""
                  ON ""Swimmers"" (""FirstName"" varchar_pattern_ops);");

            migrationBuilder.Sql(
                @"CREATE INDEX IF NOT EXISTS ""IX_Swimmers_LastNameEn_pattern""
                  ON ""Swimmers"" (""LastNameEn"" varchar_pattern_ops);");

            migrationBuilder.Sql(
                @"CREATE INDEX IF NOT EXISTS ""IX_Swimmers_FirstNameEn_pattern""
                  ON ""Swimmers"" (""FirstNameEn"" varchar_pattern_ops);");

            migrationBuilder.Sql(
                @"CREATE INDEX IF NOT EXISTS ""IX_Competitions_Name_pattern""
                  ON ""Competitions"" (""Name"" varchar_pattern_ops);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_Results_StyleId_Distance_Gender_CompDate"";");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_Swimmers_LastName_pattern"";");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_Swimmers_FirstName_pattern"";");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_Swimmers_LastNameEn_pattern"";");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_Swimmers_FirstNameEn_pattern"";");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_Competitions_Name_pattern"";");
        }
    }
}
