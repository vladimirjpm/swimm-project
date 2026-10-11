import { useEffect, useState } from 'react';
import type { InfoLang } from '../projects/components/info-popup/info-popup';

/**
 * Длинные тексты сайта на трёх языках — markdown-файлами, а не строками в коде (решение
 * Влада 11.10.2026: памятка о группах и пояснения попапов). Раскладка:
 *
 *   src/content/<раздел>/<ключ>/{en,ru,he}.md     → ключ контента «<раздел>/<ключ>»
 *
 * Пример: `src/content/help/groups/en.md` — ключ `help/groups`, страница `/help/groups`.
 *
 * Почему файлы, а не база: текст живёт в git рядом с интерфейсом, о котором он рассказывает
 * (названия кнопок в нём — те же, что на экране), правка видна в диффе. Понадобится править
 * без выкладки — эти же файлы станут начальным содержимым таблицы.
 *
 * Файлы грузятся ЛЕНИВО, по одному: в сборку каждый язык уходит отдельным куском и качается,
 * только когда его открыли. Список ключей и языков известен на этапе сборки — поэтому вкладка
 * языка без перевода гаснет до загрузки текста.
 *
 * Английский — обязательный (язык сайта и запасной вариант). Нет перевода — показываем
 * английский, вкладка погашена.
 */

const files = import.meta.glob('./**/*.md', { query: '?raw', import: 'default' }) as Record<
  string,
  () => Promise<string>
>;

const LANG_FILE = /^\.\/(.+)\/(en|ru|he)\.md$/;

/** ключ → язык → загрузчик. Собирается один раз из списка файлов сборки. */
const registry = new Map<string, Partial<Record<InfoLang, () => Promise<string>>>>();
for (const [path, load] of Object.entries(files)) {
  const m = LANG_FILE.exec(path);
  if (!m) continue;
  const [, key, lang] = m;
  const entry = registry.get(key) ?? {};
  entry[lang as InfoLang] = load;
  registry.set(key, entry);
}

/** Есть ли такой контент вообще (есть английский файл). */
export function contentExists(key: string): boolean {
  return registry.get(key)?.en != null;
}

/** На каких языках есть текст — для погашенных вкладок `UI_LangTabs`. */
export function contentLanguages(key: string): Record<InfoLang, boolean> {
  const entry = registry.get(key) ?? {};
  return { en: entry.en != null, ru: entry.ru != null, he: entry.he != null };
}

export interface ContentState {
  /** Текст markdown; null — ещё грузится или ключа нет. */
  markdown: string | null;
  /** Язык, на котором текст на самом деле показан (нет перевода → en). */
  lang: InfoLang;
  /** Ключа нет — показать «не найдено», а не вечную загрузку. */
  missing: boolean;
}

/** Текст по ключу на нужном языке; нет перевода — английский. */
export function useContent(key: string, lang: InfoLang): ContentState {
  const entry = registry.get(key);
  const shown: InfoLang = entry?.[lang] ? lang : 'en';
  const load = entry?.[shown];
  const [loaded, setLoaded] = useState<{ id: string; text: string } | null>(null);
  const id = `${key}:${shown}`;

  useEffect(() => {
    if (!load) return;
    let alive = true;
    load().then((text) => {
      if (alive) setLoaded({ id, text });
    });
    return () => {
      alive = false;
    };
  }, [id, load]);

  return {
    markdown: loaded?.id === id ? loaded.text : null,
    lang: shown,
    missing: !load,
  };
}
