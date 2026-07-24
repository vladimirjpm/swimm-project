import React from 'react';
import type { CompetitionOverview } from './types';

// Hero-модуль шапки соревнования (вариант 1b «Афиша»): фон var(--theme-primary),
// текст var(--theme-mode-accent-text) (правило парных токенов). Слева identity
// (иконка + название + мета), справа действия (Add media — только залогиненному;
// проброшено колбэком, сам модал живёт у оркестратора).

interface Props {
  title: string;
  overview: CompetitionOverview | null;
  /** undefined — кнопку не рендерим (гость или проводка ещё не подключена). */
  onAddMedia?: () => void;
}

function datesLabel(overview: CompetitionOverview | null): string | null {
  const days = overview?.days;
  if (!days?.length) return null;
  const first = days[0].date;
  const last = days[days.length - 1].date;
  return first === last ? first : `${first} – ${last}`;
}

export default function CompetitionHeaderTop({ title, overview, onAddMedia }: Props) {
  const dayCount = overview?.summary.day_count ?? 0;
  const dates = datesLabel(overview);
  const s = overview?.summary;

  return (
    <div
      className="flex flex-wrap items-center gap-3.5 px-5 pb-3.5 pt-3.5"
      style={{ background: 'var(--theme-primary)', color: 'var(--theme-mode-accent-text)' }}
    >
      {/* Иконка-плейсхолдер 64px (моб. — 44px), как у группы без icon_url */}
      <span
        className="flex h-11 w-11 shrink-0 items-center justify-center rounded-[16px] border text-[22px] md:h-16 md:w-16 md:text-[30px]"
        style={{
          borderColor: 'color-mix(in srgb, var(--theme-mode-accent-text) 35%, transparent)',
          background: 'color-mix(in srgb, var(--theme-mode-accent-text) 20%, transparent)',
        }}
      >
        🏆
      </span>

      <div className="min-w-0 flex-1">
        <div className="flex flex-wrap items-center gap-2">
          <h1 className="m-0 text-[17px] font-black leading-tight md:text-[26px]" dir="auto">
            {title}
          </h1>
          {dayCount > 1 && (
            <span
              className="rounded-full border px-2 py-0.5 text-[10px] font-extrabold uppercase tracking-wide"
              style={{
                borderColor: 'color-mix(in srgb, var(--theme-mode-accent-text) 40%, transparent)',
                background: 'color-mix(in srgb, var(--theme-mode-accent-text) 15%, transparent)',
              }}
            >
              {dayCount} Days
            </span>
          )}
        </div>
        <div className="mt-1 flex flex-wrap items-center gap-x-2.5 gap-y-0.5 text-[12.5px] font-semibold opacity-90">
          {dates && <span>{dates}</span>}
          {s && s.swimmer_count > 0 && <span>Swimmers: {s.swimmer_count}</span>}
          {s && s.club_count > 0 && <span>Clubs: {s.club_count}</span>}
          {s && <span>Results so far: {s.result_count}</span>}
        </div>
      </div>

      {onAddMedia && (
        <button
          type="button"
          onClick={onAddMedia}
          className="rounded-[10px] px-3.5 py-2 text-[12.5px] font-extrabold shadow-sm transition-opacity hover:opacity-90"
          style={{ background: 'var(--theme-mode-accent-text)', color: 'var(--theme-primary-hover, var(--theme-primary))' }}
        >
          ＋ Add media
        </button>
      )}
    </div>
  );
}
