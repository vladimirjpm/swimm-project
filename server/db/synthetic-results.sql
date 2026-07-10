-- Синтетические данные для нагрузочных тестов (фаза 3.3 «Масштаб и кэш»).
-- ТОЛЬКО для локальной/тестовой БД — не запускать на проде.
--
-- Запуск (owner-роль swimm):
--   docker exec -i swimm-postgres psql -U swimm -d swimm -f - < server/db/synthetic-results.sql
--
-- Масштаб задаётся переменными ниже: comps × per_comp строк в Results
-- (по умолчанию 600 × 5000 = 3 млн). Даты синтетики — 2016..2025, поэтому
-- реальное «последнее» соревнование (2026) остаётся последним и ?competitionId=last
-- не ломается.
--
-- Маркировка синтетики (для очистки, см. synthetic-results-cleanup.sql):
--   Competitions.Name / Clubs.Name  → 'SYNTH …'
--   Swimmers.SwimmerOrgId           → 'SYNTH-…'
--   Results.Note                    → 'SYNTH'

\set comps 600
\set per_comp 5000
\set n_swimmers 25000
\set n_clubs 300

\timing on
BEGIN;

-- Клубы
INSERT INTO "Clubs" ("Name", "NameEn")
SELECT 'SYNTH Club ' || g, 'SYNTH Club ' || g
FROM generate_series(1, :n_clubs) g;

-- Пловцы: фамилии/имена из md5-префиксов — разнообразие первых букв важно
-- для реалистичного EXPLAIN prefix-поиска (StartsWith → LIKE 'x%').
INSERT INTO "Swimmers" ("LastName", "FirstName", "LastNameEn", "FirstNameEn", "BirthYear", "Gender", "SwimmerOrgId")
SELECT
    initcap(substr(md5(g::text), 1, 8)),
    initcap(substr(md5((g * 31)::text), 1, 6)),
    initcap(substr(md5(g::text), 1, 8)),
    initcap(substr(md5((g * 31)::text), 1, 6)),
    1980 + (random() * 38)::int,
    CASE WHEN random() < 0.55 THEN 'male' ELSE 'female' END,
    'SYNTH-' || g
FROM generate_series(1, :n_swimmers) g;

-- Соревнования: равномерно по 2016..2025, шаг ~6 дней; половина 25m, половина 50m.
INSERT INTO "Competitions" ("Name", "Country", "Date", "PoolType", "IsMasters", "IsAward", "ShowCombineAllResults")
SELECT
    'SYNTH Meet ' || lpad(g::text, 4, '0'),
    'ISR',
    to_char(date '2016-01-01' + (g * 3600 / :comps), 'DD/MM/YYYY'),
    CASE WHEN g % 2 = 0 THEN '25m' ELSE '50m' END,
    false, false, false
FROM generate_series(1, :comps) g;

-- Результаты: comps × per_comp строк.
-- Дистанции взвешены по реальному распределению; время ~пропорционально дистанции.
-- ⚠ Все random() живут во ВНУТРЕННЕМ подзапросе построчно: некоррелированный
-- LATERAL (SELECT random()) Postgres вычисляет ОДИН раз на запрос — так все 3 млн строк
-- получили бы одну дистанцию (реальный баг первой версии этого скрипта).
WITH synth_comps AS (
    SELECT "Id" AS comp_id, to_date("Date", 'DD/MM/YYYY')::timestamp AS comp_date
    FROM "Competitions" WHERE "Name" LIKE 'SYNTH Meet %'
),
sw AS (
    SELECT array_agg("Id") AS ids, count(*)::int AS n
    FROM "Swimmers" WHERE "SwimmerOrgId" LIKE 'SYNTH-%'
),
cl AS (
    SELECT array_agg("Id") AS ids, count(*)::int AS n
    FROM "Clubs" WHERE "Name" LIKE 'SYNTH Club %'
),
st AS (
    SELECT array_agg("Id") AS ids, count(*)::int AS n
    FROM "Styles"
    WHERE "Name" IN ('freestyle', 'backstroke', 'breaststroke', 'butterfly', 'individual_medley')
)
INSERT INTO "Results"
    ("CompetitionId", "SwimmerId", "ClubId", "StyleId", "CompetitionDate", "Distance", "Gender",
     "AgeGroup", "EventStyleAge", "Position", "PositionAgeGroup", "Heat", "Lane",
     "TimeMillisecond", "TimeOriginal", "TimeSplit", "TimeFail", "InternationalPoints", "Note")
SELECT
    g.comp_id,
    sw.ids[1 + floor(g.r_sw * sw.n)::int],
    cl.ids[1 + floor(g.r_cl * cl.n)::int],
    st.ids[1 + floor(g.r_st * st.n)::int],
    g.comp_date,
    g.dist::text,
    CASE WHEN g.r_gender < 0.55 THEN 'male' ELSE 'female' END,
    '', '',
    1 + floor(g.r_pos * 50)::int,
    NULL,
    1 + floor(g.r_heat * 10)::int,
    1 + floor(g.r_lane * 8)::int,
    CASE WHEN g.fail THEN NULL ELSE g.ms END,
    CASE WHEN g.fail THEN 'DSQ'
         ELSE (CASE WHEN g.ms >= 60000
                    THEN (g.ms / 60000)::text || ':' || lpad(((g.ms % 60000) / 1000)::text, 2, '0')
                    ELSE ((g.ms % 60000) / 1000)::text END)
              || '.' || lpad(((g.ms % 1000) / 10)::text, 2, '0')
    END,
    '',
    g.fail,
    CASE WHEN g.fail THEN 0 ELSE 100 + floor(g.r_pts * 800)::int END,
    'SYNTH'
FROM (
    SELECT g1.*,
           (g1.dist * 650 * (0.85 + g1.r_time * 0.5))::int AS ms
    FROM (
        SELECT g0.*,
               CASE
                   WHEN g0.r_dist < 0.32 THEN 50
                   WHEN g0.r_dist < 0.62 THEN 100
                   WHEN g0.r_dist < 0.80 THEN 200
                   WHEN g0.r_dist < 0.90 THEN 400
                   WHEN g0.r_dist < 0.96 THEN 800
                   ELSE 1500
               END AS dist,
               g0.r_fail < 0.02 AS fail
        FROM (
            SELECT c.comp_id, c.comp_date,
                   random() AS r_dist, random() AS r_time, random() AS r_fail,
                   random() AS r_gender, random() AS r_st, random() AS r_sw, random() AS r_cl,
                   random() AS r_pos, random() AS r_heat, random() AS r_lane, random() AS r_pts
            FROM synth_comps c
            CROSS JOIN generate_series(1, :per_comp)
        ) g0
    ) g1
) g
CROSS JOIN sw CROSS JOIN cl CROSS JOIN st;

COMMIT;

ANALYZE "Results";
ANALYZE "Swimmers";
ANALYZE "Clubs";
ANALYZE "Competitions";

SELECT count(*) AS total_results,
       count(*) FILTER (WHERE "Note" = 'SYNTH') AS synthetic
FROM "Results";
