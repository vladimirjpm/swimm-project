using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddClubIsPseudoAndCountrySeed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsPseudo",
                table: "Clubs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // ── Сид справочника стран (alpha-3 + английское имя) ──
            // Идемпотентно: существующие коды не трогаем, кроме заглушек, где
            // CountryName был равен коду (их создавал ленивый импорт: "ISR"/"ISR").
            migrationBuilder.Sql($"""
                WITH seed(code, name) AS (VALUES {CountrySeedValues})
                INSERT INTO "Countries" ("CountryCode", "CountryName")
                SELECT code, name FROM seed
                ON CONFLICT ("CountryCode") DO UPDATE SET "CountryName" = EXCLUDED."CountryName"
                WHERE "Countries"."CountryName" = "Countries"."CountryCode";
                """);

            // ── Бэкфилл псевдоклубов (страна/сборная в графе клуба, Maccabiah) ──
            migrationBuilder.Sql("""
                -- Страна псевдоклуба — из ЕГО ИМЕНИ (перезаписываем: импорт мог повесить
                -- страну соревнования, у «USA» стоял ISR).
                UPDATE "Clubs" c
                SET "IsPseudo" = TRUE, "CountryId" = co."Id"
                FROM "Countries" co
                WHERE c."Name" NOT LIKE 'SYNTH%'
                  AND (lower(c."Name") = lower(co."CountryName") OR c."Name" = co."CountryCode");

                -- Сборные не-страны: страна неизвестна.
                UPDATE "Clubs" SET "IsPseudo" = TRUE, "CountryId" = NULL
                WHERE "Name" IN ('M25', 'Maccabiah MIX');

                -- Страна результата — от псевдоклуба-сборной, если не была указана явно.
                UPDATE "Results" r
                SET "CountryId" = c."CountryId"
                FROM "Clubs" c
                WHERE r."ClubId" = c."Id" AND c."IsPseudo"
                  AND r."CountryId" IS NULL AND c."CountryId" IS NOT NULL;

                -- Псевдоклуб — не «клуб пловца»: отвязываем, страну сохраняем.
                UPDATE "Swimmers" s
                SET "CountryId" = COALESCE(s."CountryId", c."CountryId"), "ClubId" = NULL
                FROM "Clubs" c
                WHERE s."ClubId" = c."Id" AND c."IsPseudo";
                """);
        }

        /// <summary>
        /// Страны для сида: трёхбуквенный IOC/FINA-код (как в спортивных протоколах:
        /// GER, NED, CHI — не ISO DEU/NLD/CHL) + употребимое английское имя
        /// («USA», «Great Britain»). Дополнять — просто строками.
        /// </summary>
        private const string CountrySeedValues = """
            ('ISR','Israel'),('USA','USA'),('CAN','Canada'),('MEX','Mexico'),('BRA','Brazil'),
            ('ARG','Argentina'),('CHI','Chile'),('URU','Uruguay'),('VEN','Venezuela'),('PER','Peru'),
            ('COL','Colombia'),('CRC','Costa Rica'),('PAN','Panama'),('GUA','Guatemala'),('ECU','Ecuador'),
            ('BOL','Bolivia'),('PAR','Paraguay'),('CUB','Cuba'),('DOM','Dominican Republic'),('PUR','Puerto Rico'),
            ('GBR','Great Britain'),('FRA','France'),('GER','Germany'),('NED','Netherlands'),('BEL','Belgium'),
            ('SUI','Switzerland'),('AUT','Austria'),('ITA','Italy'),('ESP','Spain'),('POR','Portugal'),
            ('SWE','Sweden'),('DEN','Denmark'),('NOR','Norway'),('FIN','Finland'),('ISL','Iceland'),
            ('IRL','Ireland'),('HUN','Hungary'),('CZE','Czech Republic'),('SVK','Slovakia'),('POL','Poland'),
            ('ROU','Romania'),('BUL','Bulgaria'),('GRE','Greece'),('TUR','Turkey'),('CYP','Cyprus'),
            ('UKR','Ukraine'),('RUS','Russia'),('BLR','Belarus'),('MDA','Moldova'),('GEO','Georgia'),
            ('ARM','Armenia'),('AZE','Azerbaijan'),('KAZ','Kazakhstan'),('UZB','Uzbekistan'),('LTU','Lithuania'),
            ('LAT','Latvia'),('EST','Estonia'),('SLO','Slovenia'),('CRO','Croatia'),('SRB','Serbia'),
            ('BIH','Bosnia and Herzegovina'),('MKD','North Macedonia'),('MNE','Montenegro'),('ALB','Albania'),('LUX','Luxembourg'),
            ('MLT','Malta'),('MON','Monaco'),('AND','Andorra'),('RSA','South Africa'),('ZIM','Zimbabwe'),
            ('EGY','Egypt'),('MAR','Morocco'),('TUN','Tunisia'),('ALG','Algeria'),('KEN','Kenya'),
            ('NGR','Nigeria'),('ETH','Ethiopia'),('AUS','Australia'),('NZL','New Zealand'),('IND','India'),
            ('CHN','China'),('JPN','Japan'),('KOR','South Korea'),('HKG','Hong Kong'),('SGP','Singapore'),
            ('THA','Thailand'),('PHI','Philippines'),('INA','Indonesia'),('MAS','Malaysia'),('VIE','Vietnam'),
            ('UAE','United Arab Emirates'),('JOR','Jordan'),('LBN','Lebanon'),('IRN','Iran'),('IRQ','Iraq'),
            ('SAU','Saudi Arabia'),('QAT','Qatar'),('KUW','Kuwait'),('BRN','Bahrain'),('OMA','Oman')
            """;

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsPseudo",
                table: "Clubs");
        }
    }
}
