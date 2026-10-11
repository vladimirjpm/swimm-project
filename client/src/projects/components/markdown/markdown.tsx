import React from 'react';
import { routes } from '../../../utils/routes';

/**
 * Markdown текстов сайта (`src/content/**`) → React-элементы. Своя маленькая реализация, а не
 * библиотека: тексты наши и пишутся под известное подмножество, а собирая элементы (не
 * `innerHTML`), мы не открываем дорогу разметке из файла в страницу.
 *
 * Подмножество (другое выводится как текст):
 * - `# Заголовок` — заголовок документа; его забирает `splitTitle`, сам рендер его пропускает;
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

function renderInline(text: string, keyPrefix: string): React.ReactNode[] {
  const out: React.ReactNode[] = [];
  let last = 0;
  let i = 0;
  for (const m of text.matchAll(INLINE)) {
    const at = m.index ?? 0;
    if (at > last) out.push(text.slice(last, at));
    const key = `${keyPrefix}-${i++}`;
    if (m[1] != null) {
      out.push(<strong key={key} className="font-bold text-[var(--t-text)]">{renderInline(m[1], key)}</strong>);
    } else {
      const href = resolveHref(m[3]);
      const external = href != null && /^https?:/i.test(href);
      out.push(href == null
        ? <React.Fragment key={key}>{renderInline(m[2], key)}</React.Fragment>
        : (
          <a key={key} href={href}
            {...(external ? { target: '_blank', rel: 'noopener noreferrer' } : {})}
            className="font-semibold text-[var(--t-accent)] underline underline-offset-2">
            {renderInline(m[2], key)}
          </a>
        ));
    }
    last = at + m[0].length;
  }
  if (last < text.length) out.push(text.slice(last));
  return out;
}

const UI_Markdown: React.FC<{ source: string; className?: string }> = ({ source, className }) => {
  const blocks = React.useMemo(() => parseBlocks(source), [source]);
  return (
    // Основной текст — роль --t-text: у --t-text-2 в светлой теме контраст ~3:1, для абзацев мало.
    <div className={`text-[15px] leading-relaxed text-[var(--t-text)] ${className ?? ''}`}>
      {blocks.map((b, n) => {
        const key = `b${n}`;
        switch (b.kind) {
          case 'h2':
            return <h2 key={key} className="mt-8 mb-2.5 text-[19px] font-semibold text-[var(--t-text)]">{renderInline(b.text, key)}</h2>;
          case 'h3':
            return <h3 key={key} className="mt-5 mb-2 text-[16px] font-semibold text-[var(--t-text)]">{renderInline(b.text, key)}</h3>;
          case 'p':
            return <p key={key} className="mb-3">{renderInline(b.text, key)}</p>;
          case 'ul':
          case 'ol': {
            const List = b.kind;
            return (
              <List key={key} className={`mb-3 ps-5 ${b.kind === 'ul' ? 'list-disc' : 'list-decimal'} space-y-1`}>
                {b.items.map((it, j) => <li key={`${key}-${j}`}>{renderInline(it, `${key}-${j}`)}</li>)}
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
