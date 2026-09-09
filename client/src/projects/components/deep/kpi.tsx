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
 * Группа в макете просит плитку помельче (24px вместо 34px) — вариант размера заведём,
 * когда будет второй потребитель, на котором его можно проверить.
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

function DeepKpi({
  label,
  value,
  hint,
  gold,
  title,
}: {
  label: string;
  value: number | string;
  hint: string;
  gold?: boolean;
  /** Нативный тултип плитки — для оговорки, которая в подпись не влезает. */
  title?: string;
}) {
  return (
    <div title={title}>
      <div
        className="text-[34px] leading-none"
        style={{
          fontFamily: 'var(--deep-font-display)',
          color: gold ? 'var(--deep-gold)' : 'var(--deep-text)',
        }}
      >
        {value}
      </div>
      <div className="mt-1 text-[11px] font-extrabold uppercase tracking-wide" style={{ color: 'var(--deep-text-mute)' }}>
        {label}
      </div>
      <div className="text-[10.5px] font-bold" style={{ color: 'var(--deep-text-ghost)' }}>
        {hint}
      </div>
    </div>
  );
}

export { DeepBadge, DeepKpi };
