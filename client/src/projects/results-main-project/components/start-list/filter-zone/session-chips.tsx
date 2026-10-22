import React from 'react';
import { sessionLabel } from '../start-list-helpers';
import type { FilterZoneMode, FilterZoneSession } from './types';

/**
 * Строка 2 зоны, чипы сессий (протокол × день). Счётчик — заявки в All, свои заплывы в
 * My plan. Выбор ОДИН на оба режима: день, открытый в программе, остаётся и в плане.
 *
 * День без моих заплывов в плане приглушён, но КЛИКАБЕЛЕН (замечание Влада 22.10.2026):
 * раньше он был `disabled`, и «Fri 23/10 · 0» просто не реагировал на тап — выглядело как
 * поломка. Теперь он открывается и карточка честно пишет, что своих в этот день нет.
 */
export default function SessionChips({ mode, sessions, activeKey, onSelect }: {
  mode: FilterZoneMode;
  sessions: FilterZoneSession[];
  activeKey: string;
  onSelect: (key: string) => void;
}) {
  const plan = mode === 'plan';
  if (sessions.length === 0) return null;

  return (
    // Мобайл: скролл край-в-край (отрицательные поля гасят паддинг панели), чтобы внутри
    // рамки не стояла полоса обрезанных чипов. pb-2 — зазор под полосой прокрутки, как в
    // строке людей; на широком экране чипы переносятся, и отступ не нужен.
    <div className="-mx-3 flex gap-1.5 overflow-x-auto px-3 pb-2 sm:mx-0 sm:flex-wrap sm:px-0 sm:pb-0">
      {sessions.map((s) => {
        const active = s.key === activeKey;
        const count = plan ? s.mine : s.entries;
        const muted = plan && s.mine === 0;
        return (
          <button
            key={s.key}
            type="button"
            aria-pressed={active}
            title={s.sourceName ?? undefined}
            onClick={() => onSelect(s.key)}
            className="flex-none whitespace-nowrap rounded-full px-3 py-[7px] text-[12.5px] font-extrabold"
            style={{
              border: `${active ? 1.5 : 1}px solid ${
                !active ? 'var(--deep-card-border)'
                  : plan ? 'var(--theme-personal-border)' : 'var(--deep-accent)'
              }`,
              background: !active ? 'transparent'
                : plan ? 'var(--theme-personal-badge-bg)' : 'var(--deep-accent-chip)',
              color: muted && !active ? 'var(--deep-text-faint)'
                : plan ? 'var(--theme-personal-accent)' : 'var(--deep-accent)',
            }}
          >
            {sessionLabel(s.dateIso, s.date, s.index)}
            <span className="ml-1.5 font-bold opacity-75">{count}</span>
          </button>
        );
      })}
    </div>
  );
}

/**
 * Прилипшая зона: на месте чипов — одна строка ТЕКСТОМ (выбранная сессия и кто в составе).
 * Оформление то же, что у активного чипа, но это спаны: ни одного действия — когда зона
 * висит поверх протокола, это справка «что сейчас показано».
 */
export function StuckSummary({ mode, session, peopleNames }: {
  mode: FilterZoneMode;
  session: FilterZoneSession | undefined;
  peopleNames: string[];
}) {
  const plan = mode === 'plan';
  return (
    <div className="flex min-w-0 flex-1 items-center gap-2 overflow-hidden whitespace-nowrap py-[3px]">
      {session && (
        <span
          className="flex-none text-[12.5px] font-extrabold"
          style={{ color: plan ? 'var(--theme-personal-accent)' : 'var(--deep-accent)' }}
        >
          {sessionLabel(session.dateIso, session.date, session.index)}
          <span className="ml-1.5 font-bold opacity-75">{plan ? session.mine : session.entries}</span>
        </span>
      )}
      {peopleNames.length > 0 && (
        <>
          <span className="flex-none opacity-30" aria-hidden>·</span>
          <span
            className="flex-none text-[10px] font-black uppercase tracking-wide"
            style={{ color: plan ? 'var(--theme-personal-accent)' : 'var(--deep-text-mute)' }}
          >
            {plan ? '⭐ Plan' : '❤️ Favorites'}
          </span>
          <span dir="auto" className="min-w-0 truncate text-[12px] font-bold" style={{ color: 'var(--deep-text-mute)' }}>
            {peopleNames.join(' · ')}
          </span>
        </>
      )}
    </div>
  );
}
