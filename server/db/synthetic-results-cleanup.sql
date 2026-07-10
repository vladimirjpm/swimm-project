-- Полная очистка синтетики, созданной synthetic-results.sql.
-- Запуск:
--   docker exec -i swimm-postgres psql -U swimm -d swimm -f - < server/db/synthetic-results-cleanup.sql

\timing on
BEGIN;

DELETE FROM "Results" WHERE "Note" = 'SYNTH';
DELETE FROM "Competitions" WHERE "Name" LIKE 'SYNTH Meet %';
DELETE FROM "Swimmers" WHERE "SwimmerOrgId" LIKE 'SYNTH-%';
DELETE FROM "Clubs" WHERE "Name" LIKE 'SYNTH Club %';

COMMIT;

VACUUM ANALYZE "Results";

SELECT count(*) AS total_results,
       count(*) FILTER (WHERE "Note" = 'SYNTH') AS synthetic
FROM "Results";
