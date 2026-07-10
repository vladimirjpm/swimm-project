-- EXPLAIN ANALYZE топ-запросов публичного API (фаза 3.3).
-- Формы запросов повторяют то, что EF Core генерирует из ResultRepository
-- (проекция ResultMapping.ToDto = JOIN на все справочники).
-- Запуск на БД с синтетикой (см. synthetic-results.sql):
--   docker exec -i swimm-postgres psql -U swimm -d swimm -f - < server/db/explain-top-queries.sql

\set page_size 51

\echo '=== Q1: paged, без фильтров, первая страница (ORDER BY CompetitionDate DESC, Position) ==='
EXPLAIN (ANALYZE, BUFFERS)
SELECT r."Id", c."Name", c."Date", c."PoolType", e."Name", st."Name",
       sw."LastName", sw."FirstName", cl."Name",
       r."Distance", r."Gender", r."Position", r."TimeMillisecond", r."TimeOriginal", r."InternationalPoints"
FROM "Results" r
JOIN "Competitions" c ON r."CompetitionId" = c."Id"
LEFT JOIN "CompetitionEvents" e ON c."EventId" = e."Id"
JOIN "Styles" st ON r."StyleId" = st."Id"
JOIN "Swimmers" sw ON r."SwimmerId" = sw."Id"
JOIN "Clubs" cl ON r."ClubId" = cl."Id"
ORDER BY r."CompetitionDate" DESC, r."Position"
LIMIT :page_size OFFSET 0;

\echo '=== Q1b: paged, без фильтров, глубокая страница (page 200) ==='
EXPLAIN (ANALYZE, BUFFERS)
SELECT r."Id"
FROM "Results" r
JOIN "Competitions" c ON r."CompetitionId" = c."Id"
JOIN "Swimmers" sw ON r."SwimmerId" = sw."Id"
JOIN "Clubs" cl ON r."ClubId" = cl."Id"
JOIN "Styles" st ON r."StyleId" = st."Id"
ORDER BY r."CompetitionDate" DESC, r."Position"
LIMIT :page_size OFFSET 9950;

\echo '=== Q2: фильтр по CompetitionId (одно синтетическое соревнование ~5000 строк) ==='
EXPLAIN (ANALYZE, BUFFERS)
SELECT r."Id", r."Position", r."TimeOriginal"
FROM "Results" r
JOIN "Competitions" c ON r."CompetitionId" = c."Id"
JOIN "Swimmers" sw ON r."SwimmerId" = sw."Id"
JOIN "Clubs" cl ON r."ClubId" = cl."Id"
JOIN "Styles" st ON r."StyleId" = st."Id"
WHERE r."CompetitionId" = (SELECT "Id" FROM "Competitions" WHERE "Name" = 'SYNTH Meet 0300')
ORDER BY r."CompetitionDate" DESC, r."Position"
LIMIT :page_size OFFSET 0;

\echo '=== Q3: фильтр по EventId (все дни события) ==='
EXPLAIN (ANALYZE, BUFFERS)
SELECT r."Id", r."Position"
FROM "Results" r
JOIN "Competitions" c ON r."CompetitionId" = c."Id"
JOIN "Swimmers" sw ON r."SwimmerId" = sw."Id"
JOIN "Clubs" cl ON r."ClubId" = cl."Id"
JOIN "Styles" st ON r."StyleId" = st."Id"
WHERE c."EventId" = (SELECT min("Id") FROM "CompetitionEvents")
ORDER BY r."CompetitionDate" DESC, r."Position"
LIMIT :page_size OFFSET 0;

\echo '=== Q4: стиль + дистанция + пол + бассейн (частый пользовательский фильтр) ==='
EXPLAIN (ANALYZE, BUFFERS)
SELECT r."Id", r."Position", r."TimeMillisecond"
FROM "Results" r
JOIN "Competitions" c ON r."CompetitionId" = c."Id"
JOIN "Swimmers" sw ON r."SwimmerId" = sw."Id"
JOIN "Clubs" cl ON r."ClubId" = cl."Id"
JOIN "Styles" st ON r."StyleId" = st."Id"
WHERE st."Name" = 'freestyle' AND r."Distance" = '100' AND r."Gender" = 'male' AND c."PoolType" = '25m'
ORDER BY r."CompetitionDate" DESC, r."Position"
LIMIT :page_size OFFSET 0;

\echo '=== Q5: подзапрос latest (самый свежий результат + EventId его соревнования) ==='
EXPLAIN (ANALYZE, BUFFERS)
SELECT r."CompetitionId", c."EventId"
FROM "Results" r
JOIN "Competitions" c ON r."CompetitionId" = c."Id"
ORDER BY r."CompetitionDate" DESC, r."CompetitionId" DESC
LIMIT 1;

\echo '=== Q6: prefix-поиск по имени пловца (4 OR StartsWith) ==='
EXPLAIN (ANALYZE, BUFFERS)
SELECT r."Id", r."Position"
FROM "Results" r
JOIN "Competitions" c ON r."CompetitionId" = c."Id"
JOIN "Swimmers" sw ON r."SwimmerId" = sw."Id"
JOIN "Clubs" cl ON r."ClubId" = cl."Id"
JOIN "Styles" st ON r."StyleId" = st."Id"
WHERE sw."LastName" LIKE 'Ab%' OR sw."FirstName" LIKE 'Ab%'
   OR sw."LastNameEn" LIKE 'Ab%' OR sw."FirstNameEn" LIKE 'Ab%'
ORDER BY r."CompetitionDate" DESC, r."Position"
LIMIT :page_size OFFSET 0;

\echo '=== Q7: prefix-поиск по клубу ==='
EXPLAIN (ANALYZE, BUFFERS)
SELECT r."Id", r."Position"
FROM "Results" r
JOIN "Competitions" c ON r."CompetitionId" = c."Id"
JOIN "Swimmers" sw ON r."SwimmerId" = sw."Id"
JOIN "Clubs" cl ON r."ClubId" = cl."Id"
JOIN "Styles" st ON r."StyleId" = st."Id"
WHERE cl."Name" LIKE 'SYNTH Club 1%' OR cl."NameEn" LIKE 'SYNTH Club 1%'
ORDER BY r."CompetitionDate" DESC, r."Position"
LIMIT :page_size OFFSET 0;

\echo '=== Q8: GetSources — count(*) результатов на соревнование (коррелированный подзапрос) ==='
EXPLAIN (ANALYZE, BUFFERS)
SELECT c."Id", (SELECT count(*) FROM "Results" r WHERE r."CompetitionId" = c."Id") AS result_count
FROM "Competitions" c
WHERE c."EventId" IS NULL;

\echo '=== Q9: карточка атлета — матч полного имени через конкатенацию ==='
EXPLAIN (ANALYZE, BUFFERS)
SELECT r."CompetitionId", r."CompetitionDate", r."Position", r."TimeMillisecond", st."Name", r."Distance"
FROM "Results" r
JOIN "Swimmers" sw ON r."SwimmerId" = sw."Id"
JOIN "Styles" st ON r."StyleId" = st."Id"
JOIN "Competitions" c ON r."CompetitionId" = c."Id"
WHERE r."RelayId" IS NULL AND (
    sw."FirstName" || ' ' || sw."LastName" = 'Sabcdef Abcdef12' OR
    sw."LastName" || ' ' || sw."FirstName" = 'Sabcdef Abcdef12' OR
    sw."FirstNameEn" || ' ' || sw."LastNameEn" = 'Sabcdef Abcdef12' OR
    sw."LastNameEn" || ' ' || sw."FirstNameEn" = 'Sabcdef Abcdef12');

\echo '=== Q10: хинт distance — DISTINCT по 3 млн строк ==='
EXPLAIN (ANALYZE, BUFFERS)
SELECT DISTINCT r."Distance" FROM "Results" r;

-- Параметры paged-режима (фаза 3.2, контракт phase3-paged-results-contract.md)

\echo '=== Q11: paged — competitionId + годы рождения (клиентский Age-фильтр) ==='
EXPLAIN (ANALYZE, BUFFERS)
SELECT r."Id", r."Position"
FROM "Results" r
JOIN "Swimmers" sw ON r."SwimmerId" = sw."Id"
WHERE r."CompetitionId" = (SELECT "Id" FROM "Competitions" WHERE "Name" = 'SYNTH Meet 0300')
  AND sw."BirthYear" BETWEEN 2005 AND 2010
ORDER BY r."CompetitionDate" DESC, r."Position"
LIMIT :page_size OFFSET 0;

\echo '=== Q12: paged — competitionId + position<=3 (podium) + COUNT total ==='
EXPLAIN (ANALYZE, BUFFERS)
SELECT r."Id"
FROM "Results" r
WHERE r."CompetitionId" = (SELECT "Id" FROM "Competitions" WHERE "Name" = 'SYNTH Meet 0300')
  AND r."Position" IS NOT NULL AND r."Position" <= 3
ORDER BY r."CompetitionDate" DESC, r."Position"
LIMIT :page_size OFFSET 0;

EXPLAIN (ANALYZE, BUFFERS)
SELECT count(*)
FROM "Results" r
WHERE r."CompetitionId" = (SELECT "Id" FROM "Competitions" WHERE "Name" = 'SYNTH Meet 0300')
  AND r."Position" IS NOT NULL AND r."Position" <= 3;
