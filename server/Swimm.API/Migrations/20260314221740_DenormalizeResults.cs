using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swimm.API.Migrations
{
    /// <inheritdoc />
    public partial class DenormalizeResults : Migration
    {
        /// <summary>
        /// Helper: drop all default constraints on a column before dropping the column itself.
        /// </summary>
        private static void DropDefaultConstraints(MigrationBuilder migrationBuilder, string table, string column)
        {
            migrationBuilder.Sql($"""
                DECLARE @sql NVARCHAR(MAX) = '';
                SELECT @sql += 'ALTER TABLE [{table}] DROP CONSTRAINT [' + dc.name + ']; '
                FROM sys.default_constraints dc
                JOIN sys.columns c ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
                WHERE dc.parent_object_id = OBJECT_ID('{table}')
                  AND c.name = '{column}';
                IF @sql <> '' EXEC sp_executesql @sql;
            """);
        }

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // --- Удалить FK и индексы EventStyle (если существуют) ---
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Results_EventStyles_EventStyleId')
                    ALTER TABLE [Results] DROP CONSTRAINT [FK_Results_EventStyles_EventStyleId];
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Results_CompetitionId_EventStyleId_EventStyleAge' AND object_id = OBJECT_ID('Results'))
                    DROP INDEX [IX_Results_CompetitionId_EventStyleId_EventStyleAge] ON [Results];
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Results_CompetitionId_Position' AND object_id = OBJECT_ID('Results'))
                    DROP INDEX [IX_Results_CompetitionId_Position] ON [Results];
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Results_EventStyleId' AND object_id = OBJECT_ID('Results'))
                    DROP INDEX [IX_Results_EventStyleId] ON [Results];
            """);

            // --- Переименовать Time -> TimeOriginal (если ещё не переименован) ---
            migrationBuilder.Sql("""
                IF COL_LENGTH('dbo.Results', 'Time') IS NOT NULL AND COL_LENGTH('dbo.Results', 'TimeOriginal') IS NULL
                    EXEC sp_rename 'dbo.Results.Time', 'TimeOriginal', 'COLUMN';
            """);

            // --- Добавить новые колонки (идемпотентно) ---
            migrationBuilder.Sql("""
                IF COL_LENGTH('dbo.Results', 'CompetitionName') IS NULL
                    ALTER TABLE [dbo].[Results] ADD [CompetitionName] NVARCHAR(300) NOT NULL DEFAULT '';
                IF COL_LENGTH('dbo.Results', 'Country') IS NULL
                    ALTER TABLE [dbo].[Results] ADD [Country] NVARCHAR(10) NOT NULL DEFAULT '';
                IF COL_LENGTH('dbo.Results', 'CompetitionDate') IS NULL
                    ALTER TABLE [dbo].[Results] ADD [CompetitionDate] DATETIME2 NOT NULL DEFAULT '0001-01-01';
                IF COL_LENGTH('dbo.Results', 'PoolType') IS NULL
                    ALTER TABLE [dbo].[Results] ADD [PoolType] NVARCHAR(5) NOT NULL DEFAULT '';
                IF COL_LENGTH('dbo.Results', 'IsMasters') IS NULL
                    ALTER TABLE [dbo].[Results] ADD [IsMasters] BIT NOT NULL DEFAULT 0;
                IF COL_LENGTH('dbo.Results', 'IsAward') IS NULL
                    ALTER TABLE [dbo].[Results] ADD [IsAward] BIT NOT NULL DEFAULT 0;
                IF COL_LENGTH('dbo.Results', 'StyleName') IS NULL
                    ALTER TABLE [dbo].[Results] ADD [StyleName] NVARCHAR(50) NOT NULL DEFAULT '';
                IF COL_LENGTH('dbo.Results', 'Distance') IS NULL
                    ALTER TABLE [dbo].[Results] ADD [Distance] NVARCHAR(20) NOT NULL DEFAULT '';
                IF COL_LENGTH('dbo.Results', 'Gender') IS NULL
                    ALTER TABLE [dbo].[Results] ADD [Gender] NVARCHAR(10) NOT NULL DEFAULT '';
                IF COL_LENGTH('dbo.Results', 'LastName') IS NULL
                    ALTER TABLE [dbo].[Results] ADD [LastName] NVARCHAR(100) NOT NULL DEFAULT '';
                IF COL_LENGTH('dbo.Results', 'FirstName') IS NULL
                    ALTER TABLE [dbo].[Results] ADD [FirstName] NVARCHAR(100) NOT NULL DEFAULT '';
                IF COL_LENGTH('dbo.Results', 'LastNameEn') IS NULL
                    ALTER TABLE [dbo].[Results] ADD [LastNameEn] NVARCHAR(100) NOT NULL DEFAULT '';
                IF COL_LENGTH('dbo.Results', 'FirstNameEn') IS NULL
                    ALTER TABLE [dbo].[Results] ADD [FirstNameEn] NVARCHAR(100) NOT NULL DEFAULT '';
                IF COL_LENGTH('dbo.Results', 'BirthYear') IS NULL
                    ALTER TABLE [dbo].[Results] ADD [BirthYear] INT NOT NULL DEFAULT 0;
                IF COL_LENGTH('dbo.Results', 'ClubName') IS NULL
                    ALTER TABLE [dbo].[Results] ADD [ClubName] NVARCHAR(200) NOT NULL DEFAULT '';
                IF COL_LENGTH('dbo.Results', 'ClubNameEn') IS NULL
                    ALTER TABLE [dbo].[Results] ADD [ClubNameEn] NVARCHAR(200) NOT NULL DEFAULT '';
                IF COL_LENGTH('dbo.Results', 'IsRelay') IS NULL
                    ALTER TABLE [dbo].[Results] ADD [IsRelay] BIT NOT NULL DEFAULT 0;
                IF COL_LENGTH('dbo.Results', 'RelayTeamName') IS NULL
                    ALTER TABLE [dbo].[Results] ADD [RelayTeamName] NVARCHAR(200) NULL;
                IF COL_LENGTH('dbo.Results', 'RelaySwimmersName') IS NULL
                    ALTER TABLE [dbo].[Results] ADD [RelaySwimmersName] NVARCHAR(500) NULL;
            """);

            // --- Backfill: заполнить денормализованные поля из справочников ---
            migrationBuilder.Sql("""
                UPDATE r SET
                    r.CompetitionName = c.Name,
                    r.Country = c.Country,
                    r.CompetitionDate = ISNULL(TRY_CONVERT(datetime2, c.Date, 103), '0001-01-01'),
                    r.PoolType = c.PoolType,
                    r.IsMasters = c.IsMasters,
                    r.IsAward = c.IsAward
                FROM dbo.Results r
                INNER JOIN dbo.Competitions c ON r.CompetitionId = c.Id
                WHERE r.CompetitionName = '';

                IF OBJECT_ID('dbo.EventStyles', 'U') IS NOT NULL
                BEGIN
                    EXEC sp_executesql N'
                        UPDATE r SET
                            r.StyleName = es.Name,
                            r.Distance = es.Distance,
                            r.Gender = es.Gender
                        FROM dbo.Results r
                        INNER JOIN dbo.EventStyles es ON r.EventStyleId = es.Id
                        WHERE r.StyleName = '''';
                    ';
                END;

                UPDATE r SET
                    r.LastName = sw.LastName,
                    r.FirstName = sw.FirstName,
                    r.LastNameEn = sw.LastNameEn,
                    r.FirstNameEn = sw.FirstNameEn,
                    r.BirthYear = sw.BirthYear
                FROM dbo.Results r
                INNER JOIN dbo.Swimmers sw ON r.SwimmerId = sw.Id
                WHERE r.LastName = '' AND r.FirstName = '';

                UPDATE r SET
                    r.ClubName = cl.Name,
                    r.ClubNameEn = cl.NameEn
                FROM dbo.Results r
                INNER JOIN dbo.Clubs cl ON r.ClubId = cl.Id
                WHERE r.ClubName = '';

                UPDATE r SET
                    r.IsRelay = CASE WHEN r.RelayId IS NOT NULL THEN 1 ELSE 0 END,
                    r.RelayTeamName = rl.TeamName,
                    r.RelaySwimmersName = rl.SwimmersName
                FROM dbo.Results r
                LEFT JOIN dbo.Relays rl ON r.RelayId = rl.Id
                WHERE r.IsRelay = 0 AND r.RelayId IS NOT NULL;
            """);

            // --- Удалить EventStyleId и Event (с предварительным удалением default constraints) ---
            DropDefaultConstraints(migrationBuilder, "Results", "EventStyleId");
            migrationBuilder.Sql("""
                IF COL_LENGTH('dbo.Results', 'EventStyleId') IS NOT NULL
                    ALTER TABLE [dbo].[Results] DROP COLUMN [EventStyleId];
            """);

            DropDefaultConstraints(migrationBuilder, "Results", "Event");
            migrationBuilder.Sql("""
                IF COL_LENGTH('dbo.Results', 'Event') IS NOT NULL
                    ALTER TABLE [dbo].[Results] DROP COLUMN [Event];
            """);

            // --- Удалить default constraints с новых колонок (чтобы не мешали в будущем) ---
            DropDefaultConstraints(migrationBuilder, "Results", "CompetitionName");
            DropDefaultConstraints(migrationBuilder, "Results", "Country");
            DropDefaultConstraints(migrationBuilder, "Results", "CompetitionDate");
            DropDefaultConstraints(migrationBuilder, "Results", "PoolType");
            DropDefaultConstraints(migrationBuilder, "Results", "IsMasters");
            DropDefaultConstraints(migrationBuilder, "Results", "IsAward");
            DropDefaultConstraints(migrationBuilder, "Results", "StyleName");
            DropDefaultConstraints(migrationBuilder, "Results", "Distance");
            DropDefaultConstraints(migrationBuilder, "Results", "Gender");
            DropDefaultConstraints(migrationBuilder, "Results", "LastName");
            DropDefaultConstraints(migrationBuilder, "Results", "FirstName");
            DropDefaultConstraints(migrationBuilder, "Results", "LastNameEn");
            DropDefaultConstraints(migrationBuilder, "Results", "FirstNameEn");
            DropDefaultConstraints(migrationBuilder, "Results", "BirthYear");
            DropDefaultConstraints(migrationBuilder, "Results", "ClubName");
            DropDefaultConstraints(migrationBuilder, "Results", "ClubNameEn");
            DropDefaultConstraints(migrationBuilder, "Results", "IsRelay");

            // --- Удалить таблицу EventStyles ---
            migrationBuilder.Sql("""
                IF OBJECT_ID('dbo.EventStyles', 'U') IS NOT NULL
                    DROP TABLE [dbo].[EventStyles];
            """);

            // --- Удалить view если существует ---
            migrationBuilder.Sql("""
                IF OBJECT_ID('dbo.vw_Results', 'V') IS NOT NULL
                    DROP VIEW [dbo].[vw_Results];
            """);

            // --- Новые индексы (идемпотентно) ---
            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Results_CompetitionDate' AND object_id = OBJECT_ID('Results'))
                    CREATE INDEX [IX_Results_CompetitionDate] ON [dbo].[Results] ([CompetitionDate]);
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Results_CompetitionId_StyleName_Distance_Gender' AND object_id = OBJECT_ID('Results'))
                    CREATE INDEX [IX_Results_CompetitionId_StyleName_Distance_Gender] ON [dbo].[Results] ([CompetitionId], [StyleName], [Distance], [Gender]);
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Results_StyleName_Distance' AND object_id = OBJECT_ID('Results'))
                    CREATE INDEX [IX_Results_StyleName_Distance] ON [dbo].[Results] ([StyleName], [Distance]);
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Results_SwimmerId_TimeMillisecond' AND object_id = OBJECT_ID('Results'))
                    CREATE INDEX [IX_Results_SwimmerId_TimeMillisecond] ON [dbo].[Results] ([SwimmerId], [TimeMillisecond]);
            """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Results_CompetitionDate' AND object_id = OBJECT_ID('Results'))
                    DROP INDEX [IX_Results_CompetitionDate] ON [dbo].[Results];
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Results_CompetitionId_StyleName_Distance_Gender' AND object_id = OBJECT_ID('Results'))
                    DROP INDEX [IX_Results_CompetitionId_StyleName_Distance_Gender] ON [dbo].[Results];
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Results_StyleName_Distance' AND object_id = OBJECT_ID('Results'))
                    DROP INDEX [IX_Results_StyleName_Distance] ON [dbo].[Results];
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Results_SwimmerId_TimeMillisecond' AND object_id = OBJECT_ID('Results'))
                    DROP INDEX [IX_Results_SwimmerId_TimeMillisecond] ON [dbo].[Results];
            """);

            migrationBuilder.DropColumn(name: "ClubName", table: "Results");
            migrationBuilder.DropColumn(name: "ClubNameEn", table: "Results");
            migrationBuilder.DropColumn(name: "CompetitionDate", table: "Results");
            migrationBuilder.DropColumn(name: "CompetitionName", table: "Results");
            migrationBuilder.DropColumn(name: "Country", table: "Results");
            migrationBuilder.DropColumn(name: "Distance", table: "Results");
            migrationBuilder.DropColumn(name: "FirstName", table: "Results");
            migrationBuilder.DropColumn(name: "FirstNameEn", table: "Results");
            migrationBuilder.DropColumn(name: "Gender", table: "Results");
            migrationBuilder.DropColumn(name: "IsAward", table: "Results");
            migrationBuilder.DropColumn(name: "IsMasters", table: "Results");
            migrationBuilder.DropColumn(name: "IsRelay", table: "Results");
            migrationBuilder.DropColumn(name: "LastName", table: "Results");
            migrationBuilder.DropColumn(name: "LastNameEn", table: "Results");
            migrationBuilder.DropColumn(name: "PoolType", table: "Results");
            migrationBuilder.DropColumn(name: "RelaySwimmersName", table: "Results");
            migrationBuilder.DropColumn(name: "RelayTeamName", table: "Results");
            migrationBuilder.DropColumn(name: "StyleName", table: "Results");
            migrationBuilder.DropColumn(name: "BirthYear", table: "Results");

            migrationBuilder.RenameColumn(name: "TimeOriginal", table: "Results", newName: "Time");

            migrationBuilder.AddColumn<int>(
                name: "EventStyleId",
                table: "Results",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Event",
                table: "Results",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "EventStyles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Distance = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Gender = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventStyles", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Results_CompetitionId_EventStyleId_EventStyleAge",
                table: "Results",
                columns: new[] { "CompetitionId", "EventStyleId", "EventStyleAge" });

            migrationBuilder.CreateIndex(
                name: "IX_Results_CompetitionId_Position",
                table: "Results",
                columns: new[] { "CompetitionId", "Position" });

            migrationBuilder.CreateIndex(
                name: "IX_Results_EventStyleId",
                table: "Results",
                column: "EventStyleId");

            migrationBuilder.CreateIndex(
                name: "IX_EventStyles_Name_Distance_Gender",
                table: "EventStyles",
                columns: new[] { "Name", "Distance", "Gender" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Results_EventStyles_EventStyleId",
                table: "Results",
                column: "EventStyleId",
                principalTable: "EventStyles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
