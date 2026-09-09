import React from 'react';

/**
 * Полоса шапки сущности — общий корпус для всех вариантов шапки (клуб, группа, дальше пловец).
 *
 * Корпус общий, содержимое — нет: каркас страницы в шапку не заглядывает вовсе (см.
 * `docs/plans/entity-page-shell-plan.md` §3.5), а варианты собирают своё из кирпичей
 * `deep/kpi.tsx`.
 *
 * `aside` — правая колонка 380px (в макете группы туда идёт фото). Не задана — левая колонка
 * занимает всю ширину, и разметка получается ровно такой, какой была у шапки клуба до выноса:
 * это нужно, чтобы переезд на каркас ничего не сдвинул.
 *
 * Порог 960px — тот же, что у пары «грид + зачёт» на странице клуба: уже него правая колонка
 * в 380px не оставляет левой места на имя и KPI.
 */

interface Props {
  children: React.ReactNode;
  /** Правая колонка 380px. null/undefined — колонки нет совсем. */
  aside?: React.ReactNode;
}

function DeepHeroBand({ children, aside }: Props) {
  return (
    <section
      className="mb-4 rounded-2xl border p-6"
      style={{
        background: 'var(--deep-hero-grad)',
        borderColor: 'var(--deep-card-border)',
      }}
    >
      {aside == null ? (
        children
      ) : (
        <div className="grid grid-cols-1 items-stretch gap-6 min-[960px]:grid-cols-[minmax(0,1fr)_380px]">
          <div className="min-w-0">{children}</div>
          <div className="min-w-0">{aside}</div>
        </div>
      )}
    </section>
  );
}

export default DeepHeroBand;
