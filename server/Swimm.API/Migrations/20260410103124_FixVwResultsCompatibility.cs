using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swimm.API.Migrations
{
    /// <inheritdoc />
    public partial class FixVwResultsCompatibility : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                EXEC('CREATE OR ALTER VIEW [dbo].[vw_Results] AS
                SELECT
                    r.Id,
                    c.Country,
                    c.Name AS Competition,
                    c.IsMasters,
                    c.IsAward,
                    r.AgeGroup,
                    c.[Date],
                    CONCAT(r.Distance, '' '', s.Name, '' '', r.Gender) AS [Event],
                    s.Name AS EventStyleName,
                    r.Distance AS EventStyleLen,
                    r.Gender AS EventStyleGender,
                    r.EventStyleAge,
                    c.PoolType,
                    r.Position,
                    r.PositionAgeGroup,
                    r.Heat,
                    r.Lane,
                    sw.LastName,
                    sw.FirstName,
                    sw.LastNameEn,
                    sw.FirstNameEn,
                    sw.BirthYear,
                    cl.Name AS Club,
                    cl.NameEn AS ClubEn,
                    r.TimeOriginal AS [Time],
                    r.TimeMillisecond,
                    r.TimeSplit,
                    r.TimeFail,
                    r.TimeFailNote,
                    r.InternationalPoints,
                    r.Note,
                    CASE WHEN r.RelayId IS NULL THEN CAST(0 AS bit) ELSE CAST(1 AS bit) END AS IsRelay,
                    rl.TeamName AS RelayTeamName,
                    rl.SwimmersName AS RelaySwimmersName
                FROM dbo.Results r
                INNER JOIN dbo.Competitions c ON r.CompetitionId = c.Id
                INNER JOIN dbo.Styles s ON r.StyleId = s.Id
                INNER JOIN dbo.Swimmers sw ON r.SwimmerId = sw.Id
                INNER JOIN dbo.Clubs cl ON r.ClubId = cl.Id
                LEFT JOIN dbo.Relays rl ON r.RelayId = rl.Id;');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF OBJECT_ID('dbo.vw_Results', 'V') IS NOT NULL
                    DROP VIEW [dbo].[vw_Results];
                """);
        }
    }
}
