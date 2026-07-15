-- dedup-report.sql — отчёт по кандидатам на склейку пловцов + сироты (read-only).
-- Запуск:  docker exec -i swimm-postgres psql -U swimm -d swimm -f - < server/db/dedup-report.sql
-- Ничего не изменяет. Требует fuzzystrmatch (trusted, ставится владельцем):
CREATE EXTENSION IF NOT EXISTS fuzzystrmatch;

-- Нормализация имени: trim + lower + ивритские финальные буквы → обычные
-- (ך→כ, ם→מ, ן→נ, ף→פ, ץ→צ), гереш ׳ и гершаим ״ → ASCII '/", двойные пробелы схлопнуть.
CREATE OR REPLACE FUNCTION pg_temp.norm(t text) RETURNS text
LANGUAGE sql IMMUTABLE AS $$
  SELECT regexp_replace(translate(lower(trim(coalesce(t, ''))), 'ךםןףץ׳״', 'כמנפצ''"'), '\s+', ' ', 'g')
$$;

-- Реальные пловцы: синтетика (SYNTH-%) исключена.
CREATE TEMP VIEW real_swimmers AS
SELECT s."Id", s."LastName", s."FirstName", s."LastNameEn", s."FirstNameEn",
       s."BirthYear", s."Gender", s."ClubId", s."Origin", s."SwimmerOrgId",
       pg_temp.norm(s."LastName" || ' ' || s."FirstName")   AS norm_he,
       pg_temp.norm(s."FirstName" || ' ' || s."LastName")   AS norm_he_swap,
       CASE WHEN s."LastNameEn" <> '' OR s."FirstNameEn" <> ''
            THEN pg_temp.norm(s."LastNameEn" || ' ' || s."FirstNameEn") END AS norm_en
FROM "Swimmers" s
WHERE s."SwimmerOrgId" IS NULL OR s."SwimmerOrgId" NOT LIKE 'SYNTH-%';

-- Число результатов у пловца (без синтетики результатов).
CREATE TEMP VIEW swimmer_stats AS
SELECT s."Id",
       (SELECT count(*) FROM "Results" r
         WHERE r."SwimmerId" = s."Id" AND coalesce(r."Note", '') <> 'SYNTH') AS result_count,
       (SELECT count(*) FROM "HubGroupMembers" m WHERE m."SwimmerId" = s."Id")     AS memberships,
       (SELECT count(*) FROM "Sys_UserFavorites" f WHERE f."SwimmerId" = s."Id")   AS favorites,
       (SELECT count(*) FROM "Sys_TrainingResults" t WHERE t."SwimmerId" = s."Id") AS trainings,
       (SELECT count(*) FROM "Sys_UserMedia" um WHERE um."SwimmerId" = s."Id")     AS user_media,
       (SELECT count(*) FROM "Sys_HubGroupMedia" gm WHERE gm."SwimmerId" = s."Id") AS group_media,
       (SELECT count(*) FROM "Sys_HubGroupUserMembers" uu WHERE uu."SwimmerId" = s."Id") AS user_members,
       (SELECT count(*) FROM "Sys_AppUsers" u WHERE u."SwimmerId" = s."Id")        AS app_users
FROM real_swimmers s;

-- Пары-кандидаты: одинаковый BirthYear, расстояние Левенштейна по нормализованному
-- полному имени (HE прямой/переставленный, EN) минимальное ≤ 2.
CREATE TEMP VIEW candidate_pairs AS
SELECT a."Id" AS id_a, b."Id" AS id_b,
       a."LastName" || ' ' || a."FirstName" AS name_a,
       b."LastName" || ' ' || b."FirstName" AS name_b,
       a."BirthYear" AS birth_year, a."Gender" AS gender_a, b."Gender" AS gender_b,
       ca."Name" AS club_a, cb."Name" AS club_b,
       sa.result_count AS results_a, sb.result_count AS results_b,
       least(
         levenshtein(a.norm_he, b.norm_he),
         levenshtein(a.norm_he, b.norm_he_swap),
         coalesce(levenshtein(a.norm_en, b.norm_en), 99)
       ) AS dist
FROM real_swimmers a
JOIN real_swimmers b
  ON a."Id" < b."Id"
 AND a."BirthYear" = b."BirthYear"
 AND abs(length(a.norm_he) - length(b.norm_he)) <= 2
LEFT JOIN "Clubs" ca ON ca."Id" = a."ClubId"
LEFT JOIN "Clubs" cb ON cb."Id" = b."ClubId"
JOIN swimmer_stats sa ON sa."Id" = a."Id"
JOIN swimmer_stats sb ON sb."Id" = b."Id"
WHERE least(
        levenshtein(a.norm_he, b.norm_he),
        levenshtein(a.norm_he, b.norm_he_swap),
        coalesce(levenshtein(a.norm_en, b.norm_en), 99)
      ) <= 2;

-- BirthYear=0 — фантомы/локальные без года: им до «уверенных» нельзя (имена-инициалы).
\echo '=== УВЕРЕННЫЕ кандидаты (dist<=1, год известен, пол не противоречит, клуб совпадает или пуст) ==='
SELECT * FROM candidate_pairs
WHERE dist <= 1
  AND birth_year <> 0
  AND (gender_a = gender_b OR gender_a IS NULL OR gender_b IS NULL)
  AND (club_a = club_b OR club_a IS NULL OR club_b IS NULL)
ORDER BY dist, name_a;

\echo '=== СПОРНЫЕ кандидаты (разный клуб/пол при dist<=1; dist=2 — только одноклубники) ==='
SELECT * FROM candidate_pairs
WHERE NOT (dist <= 1
  AND birth_year <> 0
  AND (gender_a = gender_b OR gender_a IS NULL OR gender_b IS NULL)
  AND (club_a = club_b OR club_a IS NULL OR club_b IS NULL))
  AND (dist <= 1 OR club_a = club_b)
ORDER BY dist, name_a;

\echo '=== СИРОТЫ: пловцы без результатов и любых связей (кандидаты на удаление) ==='
SELECT s."Id", s."LastName" || ' ' || s."FirstName" AS name, s."BirthYear",
       s."Origin", c."Name" AS club
FROM real_swimmers s
JOIN swimmer_stats st ON st."Id" = s."Id"
LEFT JOIN "Clubs" c ON c."Id" = s."ClubId"
WHERE st.result_count = 0 AND st.memberships = 0 AND st.favorites = 0
  AND st.trainings = 0 AND st.user_media = 0 AND st.group_media = 0
  AND st.user_members = 0 AND st.app_users = 0
  -- у пловца могут быть только синтетические результаты — это не сирота, оставляем
  AND NOT EXISTS (SELECT 1 FROM "Results" r WHERE r."SwimmerId" = s."Id")
ORDER BY s."LastName", s."FirstName";

\echo '=== Итоги ==='
SELECT
  (SELECT count(*) FROM real_swimmers) AS real_swimmers,
  (SELECT count(*) FROM candidate_pairs) AS candidate_pairs,
  (SELECT count(*) FROM candidate_pairs WHERE dist <= 1) AS dist_le_1;
