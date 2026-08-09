import React from 'react';
import type { CompetitionSource } from '../../../../utils/helpers/competition-source';
import { competitionTileData } from '../../../../utils/helpers/competition-source';
import type { CompetitionOverview } from './types';
import { PAGE_CONTAINER } from '../../../../utils/layout';
import CompetitionTile from './competition-tile';
import './competition-header-top.css';

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

  // Мобайл (14b): мета ДВУМЯ строками — дни/даты и «пул · количества». На десктопе
  // она остаётся одной лентой (см. meta выше), поэтому здесь свой набор, а не срез.
  const metaFacts: string[] = [];
  if (source?.pool_type) metaFacts.push(`${source.pool_type} pool`);
  if (source?.category === 'masters') metaFacts.push('Masters');
  if (s && s.swimmer_count > 0) metaFacts.push(`Swimmers: ${s.swimmer_count}`);
  if (s && s.club_count > 0) metaFacts.push(`Clubs: ${s.club_count}`);
  if (s) metaFacts.push(`Results: ${s.result_count}`);

  // Заголовок чемпионата — золотом (14b); у обычных стартов остаётся белым.
  const titleClass = tile.isChampionship ? ' comp-title--champ' : '';

  const addMediaButton = (extraClass: string) => onAddMedia && (
    <button
      type="button"
      onClick={onAddMedia}
      className={extraClass}
      style={{
        background: 'var(--theme-mode-accent-text)',
        color: 'var(--theme-primary-hover, var(--theme-primary))',
      }}
    >
      ＋ Add media
    </button>
  );

  const changeButton = (extraClass: string) => onChangeClick && (
    <button
      type="button"
      onClick={onChangeClick}
      className={extraClass}
      style={{
        background: 'rgba(255,255,255,0.16)',
        border: '1px solid var(--theme-mode-header-btn-border)',
        color: 'inherit',
      }}
    >
      Change <span className="text-[10px]">{changeOpen ? '▴' : '▾'}</span>
    </button>
  );

  return (
    // Фон hero — край-в-край; содержимое ограничено общим контейнером (handoff v2, 5a).
    <div style={{ background: 'var(--theme-primary)', color: 'var(--theme-mode-accent-text)' }}>
    {/* Мобайл <640px (14b): колонка «плитка+заголовок → мета в две строки → кнопки».
        Отдельная разметка, а не sm:-оверрайды: у мобайла другая СТРУКТУРА меты и кнопок,
        и держать десктоп ≥640 нетронутым проще, когда он живёт своим блоком. */}
    <div className={`${PAGE_CONTAINER} flex flex-col gap-2 pb-3 pt-3 sm:hidden`}>
      <div className="flex items-center gap-3">
        <CompetitionTile {...tile} size="xs" />
        <h1
          className="m-0 min-w-0 flex-1 text-left text-[23px] font-black"
          style={{ lineHeight: 1.18, textWrap: 'pretty' } as React.CSSProperties}
        >
          {/* dir="auto" — только на тексте: на flex-строке он развернул бы саму раскладку. */}
          <span dir="auto" className={`inline-block${titleClass}`}>{title}</span>
        </h1>
      </div>

      <div className="flex flex-col gap-0.5 text-[12px] font-semibold" style={{ opacity: 0.92 }}>
        <div className="flex flex-wrap items-center gap-x-2 gap-y-1">
          {dayCount > 1 && daysBadge}
          {dates && <span className="whitespace-nowrap">{dates}</span>}
        </div>
        {metaFacts.length > 0 && (
          <div className="flex flex-wrap items-center gap-x-1.5">
            {metaFacts.map((m, i) => (
              <span key={m} className="whitespace-nowrap">
                {i > 0 && <span className="mr-1.5 opacity-70">·</span>}
                {m}
              </span>
            ))}
          </div>
        )}
      </div>

      {/* Кнопки 36px по макету; тап-таргет ≥44px добирается паддингом строки (4+36+4). */}
      {(onAddMedia || onChangeClick) && (
        <div className="flex items-center gap-2 py-1">
          {addMediaButton(
            'flex h-9 items-center justify-center rounded-[9px] px-3 text-[12px] font-extrabold shadow-sm transition-opacity hover:opacity-90',
          )}
          {changeButton(
            'flex h-9 cursor-pointer items-center justify-center gap-1.5 whitespace-nowrap rounded-[9px] px-3 text-[12px] font-extrabold',
          )}
        </div>
      )}
    </div>

    {/* Десктоп ≥640px — без изменений.
        10b: текстовый блок flex:1 1 300px, кнопки ml-auto → при сжатии кнопки уезжают
        на свою строку, а не сдавливают заголовок в столбик (это был баг 10a). */}
    <div className={`${PAGE_CONTAINER} hidden flex-wrap items-center gap-3.5 pb-3.5 pt-3.5 sm:flex`}>
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

      <div className="ml-auto flex w-auto shrink-0 items-center gap-2">
        {addMediaButton(
          'flex min-h-[40px] flex-none items-center justify-center rounded-[10px] px-3.5 py-2 text-[12.5px] font-extrabold shadow-sm transition-opacity hover:opacity-90',
        )}
        {changeButton(
          'flex min-h-[40px] flex-none cursor-pointer items-center justify-center gap-2 whitespace-nowrap rounded-[10px] px-[16px] text-[13px] font-extrabold',
        )}
      </div>
    </div>
    </div>
  );
}
