using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Swimm.API.Migrations
{
    /// <inheritdoc />
    public partial class RenormalizeResults : Migration
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
            // === 1. Создать таблицу Styles если нет ===
            migrationBuilder.Sql("""
                IF OBJECT_ID('dbo.Styles', 'U') IS NULL
                BEGIN
                    CREATE TABLE [dbo].[Styles]
                    (
                        [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                        [Name] NVARCHAR(50) NOT NULL
                    );
                    CREATE UNIQUE INDEX [IX_Styles_Name] ON [dbo].[Styles] ([Name]);
                END;
            """);

            // === 2. Seed стили (идемпотентно) ===
            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM dbo.Styles WHERE Name = 'freestyle')
                BEGIN
                    SET IDENTITY_INSERT dbo.Styles ON;
                    INSERT INTO dbo.Styles (Id, Name) VALUES
                        (1, 'freestyle'),
                        (2, 'backstroke'),
                        (3, 'breaststroke'),
                        (4, 'butterfly'),
                        (5, 'individual_medley'),
                        (6, 'medley_relay'),
                        (7, 'free_relay');
                    SET IDENTITY_INSERT dbo.Styles OFF;
                END;
            """);

            // === 3. Добавить стили из существующих данных (если есть StyleName, которых нет в Styles) ===
            migrationBuilder.Sql("""
                IF COL_LENGTH('dbo.Results', 'StyleName') IS NOT NULL
                BEGIN
                    INSERT INTO dbo.Styles (Name)
                    SELECT DISTINCT r.StyleName
                    FROM dbo.Results r
                    WHERE r.StyleName <> ''
                      AND NOT EXISTS (SELECT 1 FROM dbo.Styles s WHERE s.Name = r.StyleName);
                END;
            """);

            // === 4. Добавить StyleId если нет ===
            migrationBuilder.Sql("""
                IF COL_LENGTH('dbo.Results', 'StyleId') IS NULL
                    ALTER TABLE [dbo].[Results] ADD [StyleId] INT NOT NULL DEFAULT 1;
            """);

            // === 5. Backfill StyleId из StyleName ===
            migrationBuilder.Sql("""
                IF COL_LENGTH('dbo.Results', 'StyleName') IS NOT NULL
                BEGIN
                    EXEC sp_executesql N'
                        UPDATE r SET r.StyleId = s.Id
                        FROM dbo.Results r
                        INNER JOIN dbo.Styles s ON s.Name = r.StyleName
                        WHERE r.StyleName <> '''';
                    ';
                END;
            """);

            // === 6. Удалить старые индексы денормализованных колонок (идемпотентно) ===
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Results_StyleName_Distance' AND object_id = OBJECT_ID('Results'))
                    DROP INDEX [IX_Results_StyleName_Distance] ON [dbo].[Results];
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Results_CompetitionId_StyleName_Distance_Gender' AND object_id = OBJECT_ID('Results'))
                    DROP INDEX [IX_Results_CompetitionId_StyleName_Distance_Gender] ON [dbo].[Results];
            """);

            // === 7. Удалить денормализованные колонки (с предварительным удалением default constraints) ===
            string[] columnsToDrop =
            [
                "CompetitionName", "Country", "PoolType", "IsMasters", "IsAward",
                "StyleName",
                "LastName", "FirstName", "LastNameEn", "FirstNameEn", "BirthYear",
                "ClubName", "ClubNameEn",
                "IsRelay", "RelayTeamName", "RelaySwimmersName"
            ];

            foreach (var col in columnsToDrop)
            {
                DropDefaultConstraints(migrationBuilder, "Results", col);
                migrationBuilder.Sql($"""
                    IF COL_LENGTH('dbo.Results', '{col}') IS NOT NULL
                        ALTER TABLE [dbo].[Results] DROP COLUMN [{col}];
                """);
            }

            // === 8. Удалить default constraint с StyleId ===
            DropDefaultConstraints(migrationBuilder, "Results", "StyleId");

            // === 9. FK на Styles (идемпотентно) ===
            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Results_Styles_StyleId')
                BEGIN
                    ALTER TABLE dbo.Results WITH CHECK
                    ADD CONSTRAINT FK_Results_Styles_StyleId
                        FOREIGN KEY (StyleId) REFERENCES dbo.Styles(Id);
                END;
            """);

            // === 10. Новые индексы (идемпотентно) ===
            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Results_StyleId_Distance' AND object_id = OBJECT_ID('Results'))
                    CREATE INDEX [IX_Results_StyleId_Distance] ON [dbo].[Results] ([StyleId], [Distance]);
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Results_CompetitionId_StyleId_Distance_Gender' AND object_id = OBJECT_ID('Results'))
                    CREATE INDEX [IX_Results_CompetitionId_StyleId_Distance_Gender] ON [dbo].[Results] ([CompetitionId], [StyleId], [Distance], [Gender]);
            """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Удалить новые индексы
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Results_StyleId_Distance' AND object_id = OBJECT_ID('Results'))
                    DROP INDEX [IX_Results_StyleId_Distance] ON [dbo].[Results];
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Results_CompetitionId_StyleId_Distance_Gender' AND object_id = OBJECT_ID('Results'))
                    DROP INDEX [IX_Results_CompetitionId_StyleId_Distance_Gender] ON [dbo].[Results];
            """);

            // Удалить FK на Styles
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Results_Styles_StyleId')
                    ALTER TABLE dbo.Results DROP CONSTRAINT FK_Results_Styles_StyleId;
            """);

            // Удалить StyleId
            DropDefaultConstraints(migrationBuilder, "Results", "StyleId");
            migrationBuilder.Sql("""
                IF COL_LENGTH('dbo.Results', 'StyleId') IS NOT NULL
                    ALTER TABLE [dbo].[Results] DROP COLUMN [StyleId];
            """);

            // Вернуть денормализованные колонки
            migrationBuilder.Sql("""
                IF COL_LENGTH('dbo.Results', 'CompetitionName') IS NULL
                    ALTER TABLE [dbo].[Results] ADD [CompetitionName] NVARCHAR(300) NOT NULL DEFAULT '';
                IF COL_LENGTH('dbo.Results', 'Country') IS NULL
                    ALTER TABLE [dbo].[Results] ADD [Country] NVARCHAR(10) NOT NULL DEFAULT '';
                IF COL_LENGTH('dbo.Results', 'PoolType') IS NULL
                    ALTER TABLE [dbo].[Results] ADD [PoolType] NVARCHAR(5) NOT NULL DEFAULT '';
                IF COL_LENGTH('dbo.Results', 'IsMasters') IS NULL
                    ALTER TABLE [dbo].[Results] ADD [IsMasters] BIT NOT NULL DEFAULT 0;
                IF COL_LENGTH('dbo.Results', 'IsAward') IS NULL
                    ALTER TABLE [dbo].[Results] ADD [IsAward] BIT NOT NULL DEFAULT 0;
                IF COL_LENGTH('dbo.Results', 'StyleName') IS NULL
                    ALTER TABLE [dbo].[Results] ADD [StyleName] NVARCHAR(50) NOT NULL DEFAULT '';
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

            // Вернуть старые индексы
            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Results_StyleName_Distance' AND object_id = OBJECT_ID('Results'))
                    CREATE INDEX [IX_Results_StyleName_Distance] ON [dbo].[Results] ([StyleName], [Distance]);
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Results_CompetitionId_StyleName_Distance_Gender' AND object_id = OBJECT_ID('Results'))
                    CREATE INDEX [IX_Results_CompetitionId_StyleName_Distance_Gender] ON [dbo].[Results] ([CompetitionId], [StyleName], [Distance], [Gender]);
            """);

            // Удалить таблицу Styles
            migrationBuilder.Sql("""
                IF OBJECT_ID('dbo.Styles', 'U') IS NOT NULL
                    DROP TABLE [dbo].[Styles];
            """);
        }
    }
}
