-- ============================================================================
-- Последовательности identity-колонок — от фактического максимума.
-- Общий шаг restore-seed.sh и sync-from-prod.sh: после заливки строк с готовыми Id
-- разъехавшаяся последовательность — это конфликт первичного ключа на первой же
-- вставке. pg_dump кладёт setval в дамп, но ошибиться тут дорого — пересчитываем.
-- Только вверх: последовательность, ушедшая дальше максимума, не откатывается.
-- ============================================================================
DO $$
DECLARE r record; mx bigint; cur bigint;
BEGIN
    FOR r IN
        SELECT c.relname AS tbl, a.attname AS col, pg_get_serial_sequence(quote_ident(c.relname), a.attname) AS seq
          FROM pg_class c
          JOIN pg_namespace n ON n.oid = c.relnamespace
          JOIN pg_attribute a ON a.attrelid = c.oid
         WHERE n.nspname = 'public' AND c.relkind = 'r' AND a.attidentity <> ''
    LOOP
        IF r.seq IS NULL THEN CONTINUE; END IF;
        EXECUTE format('SELECT coalesce(max(%I), 0) FROM %I', r.col, r.tbl) INTO mx;
        SELECT last_value INTO cur FROM pg_sequences
         WHERE schemaname = 'public' AND sequencename = split_part(r.seq, '.', 2);
        IF mx > coalesce(cur, 0) THEN
            PERFORM setval(r.seq, mx);
            RAISE NOTICE 'seq % -> %', r.seq, mx;
        END IF;
    END LOOP;
END $$;
