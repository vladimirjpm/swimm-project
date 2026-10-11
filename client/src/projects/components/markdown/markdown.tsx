import React from 'react';
import { routes } from '../../../utils/routes';

/**
 * Markdown текстов сайта (`src/content/**`) → React-элементы. Своя маленькая реализация, а не
 * библиотека: тексты наши и пишутся под известное подмножество, а собирая элементы (не
 * `innerHTML`), мы не открываем дорогу разметке из файла в страницу.
 *
 * Подмножество (другое выводится как текст):
 * - `# Заголовок` — заголовок документа; его забирает `splitTitle`, сам рендер его пропускает;
 * - строка `---` — граница сноски: всё ниже — мелкий текст под объяснением (`splitDocument`);
 * - `## …`, `### …` — подзаголовки;
 * - абзацы через пустую строку (строки одного абзаца склеиваются пробелом);
 * - списки `- пункт` и `1. пункт`;
 * - внутри строки — `**жирный**` и ссылки `[текст](адрес)`.
 *
 * Адрес ссылки: `route:<имя>` — генератор без аргументов из `routes` (правило клиента: ссылки
 * только через `routes.*()`), `https://…` / `http://…` и пути от корня (`/…`). Прочее
 * (`javascript:` и т.п.) ссылкой не становится — выводится текст.
 *
 * Направление текста задаёт вызывающий (`dir` на обёртке); списки отбиты логическим
 * `ps-*`, поэтому маркеры встают справа у иврита сами.
 *
 * Вид `page` — страницы темы deep (роли `--t-*`); `popup` — объяснялки в попапах на любых
 * страницах: там ролей `--t-*` может не быть, поэтому цвета наследуются от попапа, а ссылка —
 * `--theme-mode-accent-on-surface` (акцент как текст на поверхности режима).
 */

type Block =
  | { kind: 'h2' | 'h3' | 'p'; text: string }
  | { kind: 'ul' | 'ol'; items: string[] };

/** Заголовок документа (первая строка `# …`) и остальной текст. */
export function splitTitle(markdown: string): { title: string | null; body: string } {
  const text = markdown.replace(/\r\n?/g, '\n');
  const m = /^\s*# (.+)\n?/.exec(text);
  return m ? { title: m[1].trim(), body: text.slice(m[0].length) } : { title: null, body: text };
}

/** Заголовок, тело и сноска (всё ниже строки `---`). Сноски нет — `footnote: null`. */
export function splitDocument(markdown: string): { title: string | null; body: string; footnote: string | null } {
  const { title, body } = splitTitle(markdown);
  const padded = `\n${body}\n`;
  const m = /\n[ \t]*---[ \t]*\n/.exec(padded);
  if (!m) return { title, body, footnote: null };
  return {
    title,
    body: padded.slice(0, m.index).trim(),
    footnote: padded.slice(m.index + m[0].length).trim() || null,
  };
}

function parseBlocks(markdown: string): Block[] {
  const lines = markdown.replace(/\r\n?/g, '\n').split('\n');
  const blocks: Block[] = [];
  let para: string[] = [];
  let list: { kind: 'ul' | 'ol'; items: string[] } | null = null;

  const flush = () => {
    if (para.length) blocks.push({ kind: 'p', text: para.join(' ') });
    if (list) blocks.push(list);
    para = [];
    list = null;
  };

  for (const raw of lines) {
    const line = raw.trim();
    if (!line) { flush(); continue; }
    if (line.startsWith('# ')) { flush(); continue; } // заголовок документа — у splitTitle
    if (line.startsWith('### ')) { flush(); blocks.push({ kind: 'h3', text: line.slice(4) }); continue; }
    if (line.startsWith('## ')) { flush(); blocks.push({ kind: 'h2', text: line.slice(3) }); continue; }

    const bullet = /^- (.+)$/.exec(line);
    const numbered = /^\d+\. (.+)$/.exec(line);
    const item = bullet ?? numbered;
    if (item) {
      const kind = bullet ? 'ul' : 'ol';
      if (para.length || (list && list.kind !== kind)) flush();
      list = list ?? { kind, items: [] };
      list.items.push(item[1]);
      continue;
    }
    if (list) flush();
    para.push(line);
  }
  flush();
  return blocks;
}

/** `route:<имя>` → путь из `routes`; внешние и корневые — как есть; остальное — null. */
export function resolveHref(href: string): string | null {
  if (href.startsWith('route:')) {
    const gen = (routes as Record<string, unknown>)[href.slice(6)];
    return typeof gen === 'function' && gen.length === 0 ? String((gen as () => string)()) : null;
  }
  if (/^https?:\/\//i.test(href) || /^\/(?!\/)/.test(href)) return href;
  return null;
}

const INLINE = /\*\*(.+?)\*\*|\[([^\]]+)\]\(([^)\s]+)\)/g;

type Variant = 'page' | 'popup';

const STYLE: Record<Variant, { root: string; h2: string; h3: string; p: string; list: string; strong: string; link: string }> = {
  page: {
    // Основной текст — роль --t-text: у --t-text-2 в светлой теме контраст ~3:1, для абзацев мало.
    root: 'text-[15px] leading-relaxed text-[var(--t-text)]',
    h2: 'mt-8 mb-2.5 text-[19px] font-semibold text-[var(--t-text)]',
    h3: 'mt-5 mb-2 text-[16px] font-semibold text-[var(--t-text)]',
    p: 'mb-3',
    list: 'mb-3 ps-5 space-y-1',
    strong: 'font-bold text-[var(--t-text)]',
    link: 'font-semibold text-[var(--t-accent)] underline underline-offset-2',
  },
  popup: {
    // Кегль задаёт вызывающий: два `text-[…]` на одном элементе Tailwind разводит порядком
    // в стилях, а не в атрибуте, и сноска 11px могла бы проиграть телу 13px.
    root: 'leading-relaxed',
    h2: 'mt-4 mb-1.5 text-[14px] font-bold',
    h3: 'mt-3 mb-1 text-[13px] font-bold',
    p: 'mb-2 last:mb-0',
    list: 'mb-2 ps-5 space-y-0.5',
    strong: 'font-bold',
    link: 'font-semibold text-[var(--theme-mode-accent-on-surface)] underline underline-offset-2',
  },
};

function renderInline(text: string, keyPrefix: string, s = STYLE.page): React.ReactNode[] {
  const out: React.ReactNode[] = [];
  let last = 0;
  let i = 0;
  for (const m of text.matchAll(INLINE)) {
    const at = m.index ?? 0;
    if (at > last) out.push(text.slice(last, at));
    const key = `${keyPrefix}-${i++}`;
    if (m[1] != null) {
      out.push(<strong key={key} className={s.strong}>{renderInline(m[1], key, s)}</strong>);
    } else {
      const href = resolveHref(m[3]);
      const external = href != null && /^https?:/i.test(href);
      out.push(href == null
        ? <React.Fragment key={key}>{renderInline(m[2], key, s)}</React.Fragment>
        : (
          <a key={key} href={href}
            {...(external ? { target: '_blank', rel: 'noopener noreferrer' } : {})}
            className={s.link}>
            {renderInline(m[2], key, s)}
          </a>
        ));
    }
    last = at + m[0].length;
  }
  if (last < text.length) out.push(text.slice(last));
  return out;
}

const UI_Markdown: React.FC<{ source: string; className?: string; variant?: Variant }> = ({
  source, className, variant = 'page',
}) => {
  const blocks = React.useMemo(() => parseBlocks(source), [source]);
  const s = STYLE[variant];
  return (
    <div className={`${s.root} ${className ?? ''}`}>
      {blocks.map((b, n) => {
        const key = `b${n}`;
        switch (b.kind) {
          case 'h2':
            return <h2 key={key} className={s.h2}>{renderInline(b.text, key, s)}</h2>;
          case 'h3':
            return <h3 key={key} className={s.h3}>{renderInline(b.text, key, s)}</h3>;
          case 'p':
            return <p key={key} className={s.p}>{renderInline(b.text, key, s)}</p>;
          case 'ul':
          case 'ol': {
            const List = b.kind;
            return (
              <List key={key} className={`${s.list} ${b.kind === 'ul' ? 'list-disc' : 'list-decimal'}`}>
                {b.items.map((it, j) => <li key={`${key}-${j}`}>{renderInline(it, `${key}-${j}`, s)}</li>)}
              </List>
            );
          }
          default:
            return null;
        }
      })}
    </div>
  );
};

export default UI_Markdown;
