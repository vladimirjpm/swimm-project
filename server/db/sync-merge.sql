-- ============================================================================
-- Слияние копии прода в ЛОКАЛЬНУЮ базу — шаг sync-from-prod.sh (решение В17,
-- docs/plans/azure-deploy-plan.md §0-тер-бис). Отдельно не запускать: ждёт, что
-- скрипт уже залил строки прода в схему _sync (по таблице на каждую из
-- seed-tables.txt) и что версия схемы прода и локальной совпадает.
--
-- Почему не restore-seed.sh («удалить всё и залить»). У локальной базы есть
-- своё: аккаунты, избранное, медиа, группы, тестовые персонажи. Многие Sys_*
-- ссылаются на бизнес-таблицы с ON DELETE CASCADE — DELETE всех пловцов снёс бы
-- всё избранное, даже если те же пловцы тут же вернутся из дампа. Поэтому:
--
--   1. «Ушедшие» строки (_gone): локальные ключи, которых на проде нет. Особый
--      случай — пловцы групп (Swimmers.PrivateHubGroupId, Р71): прод их не отдаёт
--      (dump-seed.sh их вырезает), локальные остаются; но если Id локального
--      такого пловца занят на проде другим человеком — он уходит (иначе upsert
--      превратил бы его в чужого), и его тренировки/членства вместе с ним.
--      Ушедшая и та строка, что занимает второй уникальный ключ (не PK) прод-строки
--      с другим Id: одно и то же затянуто и локально, и на проде (26.10.2026).
--   2. Локальные строки вне переноса, которые держат ушедших через RESTRICT /
--      NO ACTION, — удаляются (иначе удаление ушедших упадёт). CASCADE и
--      SET NULL сделает сама база.
--   3. Переносимые строки, которые остаются, но на проде ссылаются уже на
--      другого родителя (склейка клубов, перенос результата), — перевешиваются
--      ДО удаления: иначе RESTRICT не даст удалить старого родителя.
--   4. Удаление ушедших — от детей к родителям.
--   5. Upsert всех строк прода; неизменённые строки не переписываются.
--
-- Внешние ключи на время транзакции — отложенные (как в restore-seed.sh):
-- порядок вставок неважен, а несогласованность проверится до COMMIT и откатит
-- всё целиком. Ошибка — база остаётся как была.
-- ============================================================================

\set ON_ERROR_STOP on

BEGIN;

CREATE TEMP TABLE _swimm_fk_revert ON COMMIT DROP AS
SELECT format('%I.%I', n.nspname, c.relname) AS tbl, con.conname
  FROM pg_constraint con
  JOIN pg_class c     ON c.oid = con.conrelid
  JOIN pg_namespace n ON n.oid = c.relnamespace
 WHERE con.contype = 'f'
   AND n.nspname = 'public'
   AND NOT con.condeferrable;

DO $$
DECLARE r record;
BEGIN
    FOR r IN SELECT * FROM _swimm_fk_revert LOOP
        EXECUTE format('ALTER TABLE %s ALTER CONSTRAINT %I DEFERRABLE INITIALLY DEFERRED', r.tbl, r.conname);
    END LOOP;
END $$;

SET CONSTRAINTS ALL DEFERRED;

DO $$
DECLARE
    t        record;
    fk       record;
    pk_cols  text[];
    pk_list  text;
    pk_match text;
    cols     text[];
    col_list text;
    set_list text;
    cmp_l    text;
    cmp_r    text;
    gone_sql text;
    clash    text;
    ux       record;
    n        bigint;
    n_clash  bigint;
    n_ins    bigint;
    n_upd    bigint;
    lvl_changed boolean;
    guard    int := 0;
BEGIN
    -- ── Какие таблицы переносим: те, что скрипт залил в _sync ────────────────
    CREATE TEMP TABLE _sync_tables ON COMMIT DROP AS
    SELECT c.relname AS tbl, 0 AS lvl
      FROM pg_class c JOIN pg_namespace ns ON ns.oid = c.relnamespace
     WHERE ns.nspname = '_sync' AND c.relkind = 'r';

    IF NOT EXISTS (SELECT 1 FROM _sync_tables) THEN
        RAISE EXCEPTION 'Схема _sync пуста — нечего сливать';
    END IF;

    -- Внешние ключи (одноколоночные) с родителем среди переносимых.
    CREATE TEMP TABLE _sync_fks ON COMMIT DROP AS
    SELECT ch.relname AS child, pa.relname AS parent, con.confdeltype AS deltype,
           a.attname AS fk_col, pa_a.attname AS ref_col,
           (ch.relname IN (SELECT tbl FROM _sync_tables)) AS child_synced
      FROM pg_constraint con
      JOIN pg_class ch      ON ch.oid = con.conrelid
      JOIN pg_class pa      ON pa.oid = con.confrelid
      JOIN pg_namespace ns  ON ns.oid = ch.relnamespace
      JOIN pg_attribute a   ON a.attrelid = ch.oid AND a.attnum = con.conkey[1]
      JOIN pg_attribute pa_a ON pa_a.attrelid = pa.oid AND pa_a.attnum = con.confkey[1]
     WHERE con.contype = 'f' AND ns.nspname = 'public'
       AND array_length(con.conkey, 1) = 1
       AND pa.relname IN (SELECT tbl FROM _sync_tables);

    IF EXISTS (
        SELECT 1 FROM pg_constraint con
          JOIN pg_class pa ON pa.oid = con.confrelid
          JOIN pg_namespace ns ON ns.oid = pa.relnamespace
         WHERE con.contype = 'f' AND ns.nspname = 'public' AND array_length(con.conkey, 1) > 1
           AND pa.relname IN (SELECT tbl FROM _sync_tables))
    THEN
        RAISE EXCEPTION 'Составной внешний ключ на переносимую таблицу — sync-merge.sql его не умеет';
    END IF;

    -- Уровни: родитель раньше ребёнка (самоссылки не в счёт). Цикл — отказ.
    LOOP
        UPDATE _sync_tables c SET lvl = p.lvl + 1
          FROM _sync_fks f JOIN _sync_tables p ON p.tbl = f.parent
         WHERE f.child = c.tbl AND f.child <> f.parent AND f.child_synced AND c.lvl < p.lvl + 1;
        GET DIAGNOSTICS n = ROW_COUNT;
        EXIT WHEN n = 0;
        guard := guard + 1;
        IF guard > 100 THEN RAISE EXCEPTION 'Цикл внешних ключей между переносимыми таблицами'; END IF;
    END LOOP;

    -- ── 1. Ушедшие строки ────────────────────────────────────────────────────
    FOR t IN SELECT tbl FROM _sync_tables LOOP
        SELECT array_agg(a.attname ORDER BY array_position(i.indkey::int2[], a.attnum))
          INTO pk_cols
          FROM pg_index i
          JOIN pg_attribute a ON a.attrelid = i.indrelid AND a.attnum = ANY(i.indkey)
         WHERE i.indrelid = format('public.%I', t.tbl)::regclass AND i.indisprimary;
        IF pk_cols IS NULL THEN RAISE EXCEPTION 'У таблицы % нет первичного ключа', t.tbl; END IF;

        pk_list  := (SELECT string_agg(format('%I', c), ', ') FROM unnest(pk_cols) c);
        pk_match := (SELECT string_agg(format('s.%1$I = l.%1$I', c), ' AND ') FROM unnest(pk_cols) c);

        -- Целые строки, а не только ключ: внешний ключ может ссылаться и на альтернативный
        -- ключ (Competitions.OrgCompId), и при перевешивании нужна именно его колонка.
        gone_sql := format('SELECT l.* FROM public.%I l WHERE NOT EXISTS (SELECT 1 FROM _sync.%I s WHERE %s)',
                           t.tbl, t.tbl, pk_match);
        IF t.tbl = 'Swimmers' THEN
            gone_sql := format(
                'SELECT l.* FROM public."Swimmers" l '
                'WHERE (l."PrivateHubGroupId" IS NULL AND NOT EXISTS (SELECT 1 FROM _sync."Swimmers" s WHERE s."Id" = l."Id")) '
                '   OR (l."PrivateHubGroupId" IS NOT NULL AND EXISTS (SELECT 1 FROM _sync."Swimmers" s WHERE s."Id" = l."Id"))');
        END IF;
        -- Второй уникальный ключ занят «не той» строкой: локальная строка держит ключ, который на
        -- проде у строки с ДРУГИМ Id (одно и то же затянуто и тут, и на проде — Id разошлись).
        -- Upsert идёт по Id, и прод-строка упёрлась бы в уникальный индекс (поймано 26.10.2026 на
        -- CompetitionEntries: заявки 1772/1773 затянуты дважды, Id сдвинуты на 24, 2596 строк).
        -- Такая строка — тоже ушедшая: дальше обычный путь (перевесить, удалить, upsert вставит
        -- прод-строку). Индексы-выражения пропускаем; у частичных (LogligId WHERE NOT NULL)
        -- равенство само не совпадает на NULL.
        clash := NULL;
        FOR ux IN
            SELECT array_agg(a.attname ORDER BY array_position(x.indkey::int2[], a.attnum)) AS ucols
              FROM pg_index x
              JOIN pg_attribute a ON a.attrelid = x.indrelid AND a.attnum = ANY(x.indkey)
             WHERE x.indrelid = format('public.%I', t.tbl)::regclass
               AND x.indisunique AND NOT x.indisprimary AND x.indexprs IS NULL
             GROUP BY x.indexrelid
        LOOP
            clash := concat_ws(' OR ', clash, format(
                'EXISTS (SELECT 1 FROM _sync.%I s WHERE %s AND ROW(%s) IS DISTINCT FROM ROW(%s))',
                t.tbl,
                (SELECT string_agg(format('s.%1$I = l.%1$I', c), ' AND ') FROM unnest(ux.ucols) c),
                (SELECT string_agg(format('s.%I', c), ', ') FROM unnest(pk_cols) c),
                (SELECT string_agg(format('l.%I', c), ', ') FROM unnest(pk_cols) c)));
        END LOOP;
        IF clash IS NOT NULL THEN
            EXECUTE format('SELECT count(*) FROM public.%I l WHERE %s', t.tbl, clash) INTO n_clash;
            IF n_clash > 0 THEN
                -- Дописываем в тот же WHERE (у обоих видов gone_sql он последний), а не UNION:
                -- у колонок json нет равенства, и UNION бы упал.
                gone_sql := gone_sql || ' OR (' || clash || ')';
                RAISE NOTICE 'ключ занят другой строкой прода: % — % (уйдут, прод-строки встанут на место)',
                             t.tbl, n_clash;
            END IF;
        END IF;

        EXECUTE format('CREATE TEMP TABLE %I ON COMMIT DROP AS %s', '_gone_' || t.tbl, gone_sql);
        EXECUTE format('SELECT count(*) FROM %I', '_gone_' || t.tbl) INTO n;
        IF n > 0 THEN RAISE NOTICE 'уходит: % — %', t.tbl, n; END IF;
    END LOOP;

    -- ── 2. Локальное вне переноса, держащее ушедших через RESTRICT / NO ACTION ──
    FOR fk IN SELECT * FROM _sync_fks WHERE NOT child_synced AND deltype IN ('r', 'a') LOOP
        EXECUTE format('DELETE FROM public.%I c WHERE c.%I IN (SELECT g.%I FROM %I g)',
                       fk.child, fk.fk_col, fk.ref_col, '_gone_' || fk.parent);
        GET DIAGNOSTICS n = ROW_COUNT;
        IF n > 0 THEN RAISE NOTICE 'локальное без родителя удалено: %.% — %', fk.child, fk.fk_col, n; END IF;
    END LOOP;

    -- ── 3. Перевесить остающихся на нового родителя (до удаления старого) ────
    FOR fk IN SELECT * FROM _sync_fks WHERE child_synced LOOP
        SELECT array_agg(a.attname) INTO pk_cols
          FROM pg_index i JOIN pg_attribute a ON a.attrelid = i.indrelid AND a.attnum = ANY(i.indkey)
         WHERE i.indrelid = format('public.%I', fk.child)::regclass AND i.indisprimary;
        pk_match := (SELECT string_agg(format('s.%1$I = c.%1$I', c), ' AND ') FROM unnest(pk_cols) c);
        EXECUTE format(
            'UPDATE public.%1$I c SET %2$I = s.%2$I FROM _sync.%1$I s '
            'WHERE %3$s AND c.%2$I IS DISTINCT FROM s.%2$I AND c.%2$I IN (SELECT g.%4$I FROM %5$I g)',
            fk.child, fk.fk_col, pk_match, fk.ref_col, '_gone_' || fk.parent);
        GET DIAGNOSTICS n = ROW_COUNT;
        IF n > 0 THEN RAISE NOTICE 'перевешено: %.% — %', fk.child, fk.fk_col, n; END IF;
    END LOOP;

    -- ── 4. Удалить ушедших: дети раньше родителей ────────────────────────────
    FOR t IN SELECT tbl FROM _sync_tables ORDER BY lvl DESC, tbl LOOP
        SELECT array_agg(a.attname) INTO pk_cols
          FROM pg_index i JOIN pg_attribute a ON a.attrelid = i.indrelid AND a.attnum = ANY(i.indkey)
         WHERE i.indrelid = format('public.%I', t.tbl)::regclass AND i.indisprimary;
        pk_list := (SELECT string_agg(format('%I', c), ', ') FROM unnest(pk_cols) c);
        EXECUTE format('DELETE FROM public.%I WHERE (%s) IN (SELECT %s FROM %I)',
                       t.tbl, pk_list, pk_list, '_gone_' || t.tbl);
    END LOOP;

    -- ── 5. Upsert: родители раньше детей (проверки FK всё равно отложены) ────
    FOR t IN SELECT tbl FROM _sync_tables ORDER BY lvl, tbl LOOP
        SELECT array_agg(a.attname) INTO pk_cols
          FROM pg_index i JOIN pg_attribute a ON a.attrelid = i.indrelid AND a.attnum = ANY(i.indkey)
         WHERE i.indrelid = format('public.%I', t.tbl)::regclass AND i.indisprimary;
        pk_list := (SELECT string_agg(format('%I', c), ', ') FROM unnest(pk_cols) c);

        -- Колонки: живые, не вычисляемые (их считает база), есть в копии прода.
        SELECT array_agg(a.attname ORDER BY a.attnum) INTO cols
          FROM pg_attribute a
         WHERE a.attrelid = format('public.%I', t.tbl)::regclass
           AND a.attnum > 0 AND NOT a.attisdropped AND a.attgenerated = ''
           AND EXISTS (SELECT 1 FROM pg_attribute s
                        WHERE s.attrelid = format('_sync.%I', t.tbl)::regclass
                          AND s.attname = a.attname AND NOT s.attisdropped);

        col_list := (SELECT string_agg(format('%I', c), ', ') FROM unnest(cols) c);
        set_list := (SELECT string_agg(format('%1$I = EXCLUDED.%1$I', c), ', ')
                       FROM unnest(cols) c WHERE c <> ALL (pk_cols));
        cmp_l    := (SELECT string_agg(format('public.%I.%I', t.tbl, c), ', ') FROM unnest(cols) c WHERE c <> ALL (pk_cols));
        cmp_r    := (SELECT string_agg(format('EXCLUDED.%I', c), ', ') FROM unnest(cols) c WHERE c <> ALL (pk_cols));

        EXECUTE format(
            'WITH up AS (INSERT INTO public.%1$I (%2$s) %3$s SELECT %2$s FROM _sync.%1$I '
            'ON CONFLICT (%4$s) %5$s RETURNING (xmax = 0) AS ins) '
            'SELECT count(*) FILTER (WHERE ins), count(*) FILTER (WHERE NOT ins) FROM up',
            t.tbl, col_list,
            CASE WHEN EXISTS (SELECT 1 FROM pg_attribute a
                               WHERE a.attrelid = format('public.%I', t.tbl)::regclass AND a.attidentity = 'a')
                 THEN 'OVERRIDING SYSTEM VALUE' ELSE '' END,
            pk_list,
            CASE WHEN set_list IS NULL THEN 'DO NOTHING'
                 ELSE format('DO UPDATE SET %s WHERE (%s) IS DISTINCT FROM (%s)', set_list,
                             -- ROW(...) — иначе одна колонка сравнивалась бы скаляром со строкой
                             'ROW(' || cmp_l || ')', 'ROW(' || cmp_r || ')') END)
        INTO n_ins, n_upd;
        IF n_ins + n_upd > 0 THEN
            RAISE NOTICE 'обновлено: % — новых %, изменённых %', t.tbl, n_ins, n_upd;
        END IF;
    END LOOP;
END $$;

-- Здесь срабатывают все отложенные проверки: несогласованность — откат целиком.
SET CONSTRAINTS ALL IMMEDIATE;

DO $$
DECLARE r record;
BEGIN
    FOR r IN SELECT * FROM _swimm_fk_revert LOOP
        EXECUTE format('ALTER TABLE %s ALTER CONSTRAINT %I NOT DEFERRABLE', r.tbl, r.conname);
    END LOOP;
END $$;

SET LOCAL client_min_messages = warning;  -- «drop cascades to N objects» — не новость
DROP SCHEMA _sync CASCADE;

COMMIT;
