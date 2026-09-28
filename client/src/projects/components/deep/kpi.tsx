import React from 'react';

/**
 * Кирпичики шапки сущности: бейдж-пилюля и плитка KPI.
 *
 * Жили приватными функциями внутри `club-project/components/club-hero.tsx`, пока шапка была
 * одна. Шапок стало несколько (клуб, группа, дальше пловец) и они непохожи — общей делается
 * не сама шапка, а её кирпичи: иначе каждый вариант перерисует пилюлю по-своему и они
 * разъедутся на первой же правке токенов.
 *
 * Формы менять нельзя без повода: числа и подписи выверены на живой странице клуба.
 * Мелкая плитка (24px) — это телефон, а не отдельный вариант: размер решает ширина.
 */

function DeepBadge({ children, accent }: { children: React.ReactNode; accent?: boolean }) {
  return (
    <span
      className="rounded-full border px-3 py-1 text-[11.5px] font-extrabold"
      style={{
        background: accent ? 'var(--deep-accent-chip)' : 'var(--deep-card-bg-raised)',
        borderColor: accent ? 'var(--deep-accent-border)' : 'var(--deep-card-border)',
        color: accent ? 'var(--deep-accent)' : 'var(--deep-text-mute)',
      }}
    >
      {children}
    </span>
  );
}

/**
 * Плитка KPI. Размер — по ширине (хендофф group-club-changes §1, §2): на телефоне плитка
 * компактная (24px, подписи 9.5px, свой паддинг — плитки стоят сеткой по три), от 960px —
 * прежняя крупная (34px) в ряд через 32px.
 */
function DeepKpi({
  label,
  shortLabel,
  value,
  hint,
  gold,
  title,
}: {
  label: string;
  /** Подпись для телефона, где плитка — треть экрана («Wins» вместо «Championship wins»). */
  shortLabel?: string;
  value: number | string;
  hint: string;
  gold?: boolean;
  /** Нативный тултип плитки — для оговорки, которая в подпись не влезает. */
  title?: string;
}) {
  return (
    <div title={title} className="min-w-0 px-2.5 pb-[9px] pt-2.5 min-[960px]:p-0">
      <div
        className="text-[24px] leading-none min-[960px]:text-[34px]"
        style={{
          fontFamily: 'var(--deep-font-display)',
          color: gold ? 'var(--deep-gold)' : 'var(--deep-text)',
        }}
      >
        {value}
      </div>
      <div
        className="mt-1 truncate text-[9.5px] font-extrabold uppercase tracking-wide min-[960px]:text-[11px]"
        style={{ color: 'var(--deep-text-mute)' }}
      >
        {shortLabel ? (
          <>
            <span className="min-[960px]:hidden">{shortLabel}</span>
            <span className="max-[959px]:hidden">{label}</span>
          </>
        ) : label}
      </div>
      <div className="truncate text-[9.5px] font-bold min-[960px]:text-[10.5px]" style={{ color: 'var(--deep-text-ghost)' }}>
        {hint}
      </div>
    </div>
  );
}

/**
 * Плитка-ссылка «28 Competitions →» — последняя в ряду KPI. Заменила отдельную кнопку
 * «Competitions →» справа от имени: цифра и переход в одном месте, и в строке имени
 * ничего не стоит. Акцентный чип — чтобы читалась как нажимаемая, а не как ещё одна цифра.
 *
 * `href` — переход на другой экран (группа → `/groups/{slug}/results`), `onClick` — на таб
 * этой же страницы (клуб → History).
 */
function DeepKpiLink({
  label, value, hint, href, onClick,
}: {
  label: string;
  value: number | string;
  hint: string;
  href?: string;
  onClick?: () => void;
}) {
  const body = (
    <>
      <div className="text-[24px] leading-none min-[960px]:text-[34px]" style={{ fontFamily: 'var(--deep-font-display)' }}>
        {value}
      </div>
      {/* Без truncate: «Competitions →» на телефоне шире трети экрана, и обрезанная подпись
          теряла стрелку — единственный знак, что плитка нажимается. Переносится на 2 строки. */}
      <div className="mt-1 text-[9.5px] font-extrabold uppercase leading-tight tracking-wide min-[960px]:text-[11px]">
        {label}
      </div>
      <div className="truncate text-[9.5px] font-bold opacity-70 min-[960px]:text-[10.5px]">{hint}</div>
    </>
  );
  // На десктопе плитка выходит за свой бокс отрицательным полем: цифра стоит в линию с
  // соседними, а рамка чипа — вокруг неё.
  const cls = 'block min-w-0 rounded-[12px] border px-2.5 pb-[9px] pt-2.5 text-left no-underline min-[960px]:-mx-3 min-[960px]:-my-2 min-[960px]:px-3 min-[960px]:py-2';
  const style = {
    borderColor: 'var(--deep-accent-border)',
    background: 'var(--deep-accent-chip)',
    color: 'var(--deep-accent)',
  };
  if (href) return <a href={href} className={cls} style={style}>{body}</a>;
  return <button type="button" onClick={onClick} className={`${cls} cursor-pointer`} style={style}>{body}</button>;
}

/**
 * Ряд KPI: телефон — сетка по три колонки (у клуба шесть плиток встают 3×2), десктоп —
 * ряд через 32px. Плитки с нулём вызывающий не кладёт («Gold 0» читался как упрёк).
 */
function DeepKpiRow({ children }: { children: React.ReactNode }) {
  return (
    <div className="mt-3 grid grid-cols-3 gap-1.5 min-[960px]:mt-6 min-[960px]:flex min-[960px]:flex-wrap min-[960px]:gap-8">
      {children}
    </div>
  );
}

export { DeepBadge, DeepKpi, DeepKpiLink, DeepKpiRow };
