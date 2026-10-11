import React, { useEffect, useRef, useState } from 'react';

/**
 * Кирпичи строки «кто это» шапки сущности: имя, подзаголовок и чип-меню членства.
 * Общие у клуба и группы (дальше пловец) — хендофф group-club-changes §1–2, варианты 2a/4a/7a.
 *
 * Имя: ИВРИТСКОЕ по умолчанию (правило проекта), но не всегда — поэтому `dir="auto"`, а не
 * `rtl` из хендоффа: жёсткий rtl на латинском имени переставлял скобки («[TEST] Open» читалось
 * «Open [TEST]»). Выравнивание ЛЕВОЕ в обоих случаях — в линию с остальной шапкой. До этого стоял `truncate`: у RTL-строки многоточие
 * встаёт в логический КОНЕЦ, то есть слева, и имя резалось с НАЧАЛА («…פין נתניה מסטרס»).
 * Теперь имя переносится, до двух строк; `line-clamp` — только страховка от совсем длинных.
 */

type TitleSize = 'group' | 'club';

/** Кегль по вариантам: мобайл 22px у обоих, десктоп 34px у группы и 40px у клуба. */
const TITLE_SIZE: Record<TitleSize, string> = {
  group: 'text-[22px] leading-[1.2] min-[960px]:text-[34px] min-[960px]:leading-[1.15]',
  club: 'text-[22px] leading-[1.2] min-[960px]:text-[40px] min-[960px]:leading-[1.12]',
};

function DeepHeroTitle({ children, size }: { children: React.ReactNode; size: TitleSize }) {
  return (
    <h1
      dir="auto"
      className={`m-0 line-clamp-2 text-left font-normal [overflow-wrap:anywhere] ${TITLE_SIZE[size]}`}
      style={{ fontFamily: 'var(--deep-font-display)', color: 'var(--deep-text)' }}
    >
      {children}
    </h1>
  );
}

/**
 * Подзаголовок: `{name_en} · {флаг} {город}` одной строкой вместо отдельного бейджа
 * «флаг + город» — тот занимал на телефоне собственный ряд. Части собирает вызывающий;
 * ивритские куски (город) он оборачивает в `<bdi>`, иначе RTL-перестановка утащит
 * разделитель «·» не на ту сторону.
 */
function DeepHeroSubline({ parts }: { parts: React.ReactNode[] }) {
  const shown = parts.filter((p) => p != null && p !== false && p !== '');
  if (shown.length === 0) return null;
  return (
    <span className="text-[12px] font-bold min-[960px]:text-[13px]" style={{ color: 'var(--deep-text-mute)' }}>
      {shown.map((p, i) => (
        <React.Fragment key={i}>
          {i > 0 && ' · '}
          {p}
        </React.Fragment>
      ))}
    </span>
  );
}

export interface DeepMenuItem {
  label: string;
  onSelect: () => void;
  /** Опасное действие (выйти, отписаться) — красный оттенок. */
  danger?: boolean;
}

/**
 * Чип состояния с меню: «✓ Member ⋯» → Leave group, «✓ Following» → Unfollow.
 *
 * Меню открывает ВЕСЬ чип, а не только «⋯»: на телефоне ⋯ — квадрат 22px, в него не попасть.
 * Действие из меню — единственное (выйти / отписаться), поэтому отдельной кнопки
 * «Leave group» в шапке больше нет: раньше она занимала место наравне с главными.
 *
 * `variant`: `member` — нейтральный чип 32px с квадратиком ⋯ (группа); `follow` — акцентный
 * чип под логотипом клуба (26px мобайл, 30px десктоп), без ⋯.
 */
function DeepMenuChip({
  label, items, variant, busy, title,
}: {
  label: string;
  items: DeepMenuItem[];
  variant: 'member' | 'follow';
  busy?: boolean;
  title?: string;
}) {
  const [open, setOpen] = useState(false);
  const rootRef = useRef<HTMLDivElement>(null);

  // Закрытие по клику мимо и по Escape — меню одно, стек не нужен.
  useEffect(() => {
    if (!open) return undefined;
    const onDown = (e: MouseEvent) => {
      if (!rootRef.current?.contains(e.target as Node)) setOpen(false);
    };
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') setOpen(false); };
    document.addEventListener('mousedown', onDown);
    document.addEventListener('keydown', onKey);
    return () => {
      document.removeEventListener('mousedown', onDown);
      document.removeEventListener('keydown', onKey);
    };
  }, [open]);

  const member = variant === 'member';

  return (
    <div ref={rootRef} className={`relative ${member ? 'inline-flex' : 'flex w-full'}`}>
      <button
        type="button"
        disabled={busy}
        aria-haspopup="menu"
        aria-expanded={open}
        title={title}
        onClick={() => setOpen((v) => !v)}
        className={
          member
            ? 'hp-mono flex h-8 items-center gap-2 rounded-[10px] border pl-3 pr-1.5 text-[12px] font-extrabold disabled:opacity-50'
            : 'hp-mono flex h-[26px] w-full items-center justify-center rounded-[8px] border px-1 text-[10.5px] font-extrabold disabled:opacity-50 min-[960px]:h-[30px] min-[960px]:rounded-[9px] min-[960px]:text-[12px]'
        }
        style={member
          ? { borderColor: 'var(--deep-card-border)', background: 'var(--deep-card-bg)', color: 'var(--deep-text-mute)' }
          : { borderColor: 'var(--deep-accent-border)', background: 'var(--deep-accent-chip)', color: 'var(--deep-accent)' }}
      >
        {label}
        {member && (
          <span
            aria-hidden="true"
            className="flex h-[22px] w-[22px] items-center justify-center rounded-[6px] text-[13px] leading-none"
            style={{ background: 'var(--deep-divider)' }}
          >
            ⋯
          </span>
        )}
      </button>

      {open && (
        <div
          role="menu"
          className="absolute left-0 top-[calc(100%+6px)] z-20 w-[170px] rounded-[12px] border p-1.5"
          style={{
            background: 'var(--deep-card-bg)',
            borderColor: 'var(--deep-card-border)',
            boxShadow: 'var(--deep-card-shadow)',
          }}
        >
          {items.map((item) => (
            <button
              key={item.label}
              type="button"
              role="menuitem"
              onClick={() => { setOpen(false); item.onSelect(); }}
              className="w-full rounded-[8px] border-0 p-2.5 text-left text-[13px] font-extrabold"
              style={item.danger
                ? { background: 'var(--deep-danger-soft)', color: 'var(--deep-danger)' }
                : { background: 'transparent', color: 'var(--deep-text)' }}
            >
              {item.label}
            </button>
          ))}
        </div>
      )}
    </div>
  );
}

/**
 * Кнопка-призыв там же, где у своего стоит чип-меню: «+ Join group», «+ Follow». Гостю она
 * ведёт во вход (фича видна, но требует логина — как сердечко в таблице результатов).
 * `size` повторяет размер чипа, место которого она занимает.
 */
function DeepCtaChip({
  children, onClick, busy, title, size, filled,
}: {
  children: React.ReactNode;
  onClick: () => void;
  busy?: boolean;
  title?: string;
  size: 'member' | 'follow';
  /** Заливка акцентом (главный призыв) вместо акцентного чипа. */
  filled?: boolean;
}) {
  return (
    <button
      type="button"
      disabled={busy}
      onClick={onClick}
      title={title}
      className={
        size === 'member'
          ? 'hp-mono inline-flex h-8 items-center rounded-[10px] border px-3 text-[12px] font-extrabold hover:brightness-110 disabled:opacity-50'
          : 'hp-mono flex h-[26px] w-full items-center justify-center rounded-[8px] border px-1 text-[10.5px] font-extrabold hover:brightness-110 disabled:opacity-50 min-[960px]:h-[30px] min-[960px]:rounded-[9px] min-[960px]:text-[12px]'
      }
      style={filled
        ? { background: 'var(--deep-accent)', borderColor: 'var(--deep-accent)', color: 'var(--deep-accent-ink)' }
        : { background: 'var(--deep-accent-chip)', borderColor: 'var(--deep-accent-border)', color: 'var(--deep-accent)' }}
    >
      {children}
    </button>
  );
}

export { DeepHeroTitle, DeepHeroSubline, DeepMenuChip, DeepCtaChip };
