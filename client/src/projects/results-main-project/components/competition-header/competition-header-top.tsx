import React from 'react';
import type { CompetitionSource } from '../../../../utils/helpers/competition-source';
import { competitionTileData } from '../../../../utils/helpers/competition-source';
import type { CompetitionOverview } from './types';
import { PAGE_CONTAINER } from '../../../../utils/layout';
import CompetitionTile from './competition-tile';

// Hero-модуль шапки соревнования (вариант 1b «Афиша»): фон var(--theme-primary),
// текст var(--theme-mode-accent-text) (правило парных токенов). Слева identity
// (иконка + название + мета), справа действия (Add media — только залогиненному;
// проброшено колбэком, сам модал живёт у оркестратора).

interface Props {
  title: string;
  overview: CompetitionOverview | null;
  /** Выбранный источник из селектора (bассейн/сезон/бейджи) — когда шапка живёт внутри DDL. */
  source?: CompetitionSource;
  /** undefined — кнопку не рендерим (гость или проводка ещё не подключена). */
  onAddMedia?: () => void;
  /** «Change ▾» — toggle панели селектора (проп DDL renderHeader). */
  onChangeClick?: () => void;
  changeOpen?: boolean;
}

function datesLabel(overview: CompetitionOverview | null): string | null {
  const days = overview?.days;
  if (!days?.length) return null;
  const first = days[0].date;
  const last = days[days.length - 1].date;
  return first === last ? first : `${first} – ${last}`;
}

export default function CompetitionHeaderTop({
  title, overview, source, onAddMedia, onChangeClick, changeOpen,
}: Props) {
  const dayCount = overview?.summary.day_count ?? 0;
  const dates = datesLabel(overview);
  const s = overview?.summary;
  const tile = competitionTileData(source);

  // Мета одной лентой: каждый пункт nowrap, перенос — только между пунктами (10b).
  const meta: string[] = [];
  if (dates) meta.push(dates);
  if (source?.pool_type) meta.push(`${source.pool_type} pool`);
  if (source?.category === 'masters') meta.push('Masters');
  if (s && s.swimmer_count > 0) meta.push(`Swimmers: ${s.swimmer_count}`);
  if (s && s.club_count > 0) meta.push(`Clubs: ${s.club_count}`);
  if (s) meta.push(`Results so far: ${s.result_count}`);

  const daysBadge = (
    <span
      className="rounded-full border px-2 py-0.5 text-[10px] font-extrabold uppercase tracking-wide"
      style={{
        borderColor: 'color-mix(in srgb, var(--theme-mode-accent-text) 40%, transparent)',
        background: 'color-mix(in srgb, var(--theme-mode-accent-text) 15%, transparent)',
      }}
    >
      {dayCount} Days
    </span>
  );

  return (
    // Фон hero — край-в-край; содержимое ограничено общим контейнером (handoff v2, 5a).
    <div style={{ background: 'var(--theme-primary)', color: 'var(--theme-mode-accent-text)' }}>
    {/* 10b: текстовый блок flex:1 1 300px, кнопки ml-auto → при сжатии кнопки уезжают
        на свою строку, а не сдавливают заголовок в столбик (это был баг 10a). */}
    <div className={`${PAGE_CONTAINER} flex flex-wrap items-center gap-3.5 pb-3.5 pt-3.5`}>
      <div className="flex min-w-0 flex-[1_1_300px] items-center gap-3">
        <CompetitionTile {...tile} />

        <div className="min-w-0 flex-1">
          <h1
            // dir="auto" делает ивритский заголовок RTL — это правильно для порядка слов,
            // но выравнивание тянет вправо: в шапке заголовок всегда прижат к началу строки.
            className="m-0 min-w-0 text-left text-[19px] font-black leading-tight md:text-[26px]"
            style={{ textWrap: 'pretty' } as React.CSSProperties}
            dir="auto"
          >
            {title}
          </h1>
          {/* Бейдж «N Days» — всегда в начале меты (одна версия на десктоп и мобайл) */}
          <div className="mt-1 flex flex-wrap items-center gap-x-2.5 gap-y-0.5 text-[12.5px] font-semibold opacity-90">
            {dayCount > 1 && daysBadge}
            {meta.map((m) => (
              <span key={m} className="whitespace-nowrap">{m}</span>
            ))}
          </div>
        </div>
      </div>

      {/* Мобайл: кнопки своей строкой, тап-таргет ≥44px */}
      <div className="flex w-full shrink-0 items-center gap-2 sm:ml-auto sm:w-auto">
        {onAddMedia && (
          <button
            type="button"
            onClick={onAddMedia}
            className="flex min-h-[44px] flex-1 items-center justify-center rounded-[10px] px-3.5 py-2 text-[12.5px] font-extrabold shadow-sm transition-opacity hover:opacity-90 sm:min-h-[40px] sm:flex-none"
            style={{ background: 'var(--theme-mode-accent-text)', color: 'var(--theme-primary-hover, var(--theme-primary))' }}
          >
            ＋ Add media
          </button>
        )}
        {onChangeClick && (
          <button
            type="button"
            onClick={onChangeClick}
            className="flex min-h-[44px] flex-1 cursor-pointer items-center justify-center gap-2 whitespace-nowrap rounded-[10px] px-[16px] text-[13px] font-extrabold sm:min-h-[40px] sm:flex-none"
            style={{
              background: 'rgba(255,255,255,0.16)',
              border: '1px solid var(--theme-mode-header-btn-border)',
              color: 'inherit',
            }}
          >
            Change <span className="text-[10px]">{changeOpen ? '▴' : '▾'}</span>
          </button>
        )}
      </div>
    </div>
    </div>
  );
}
