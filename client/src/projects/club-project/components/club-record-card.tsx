import React from 'react';
import UI_SwimTime, { SwimQuality } from '../../components/mix/swim-time/swim-time';

/**
 * Общая карточка «стена времён» — оболочка + плитка, на которых собраны ОБЕ карточки
 * времён страницы клуба:
 *  • `club-records.tsx` — Season best (наши протоколы, скоуп по сезону);
 *  • `club-record-wall.tsx` — Record wall (официальный справочник Records).
 *
 * Данные у них разные (у одной пловец с ссылкой и год, у другой ступень рекорда и
 * держатель строкой), поэтому общее здесь — форма, а не содержимое: шапка со счётчиком и
 * переключателем бассейна плюс плитка с фиксированными слотами. Что положить в слоты,
 * решает карточка-владелец.
 *
 * Переключатель 25м/50м живёт тут, потому что он физический, а не про скоуп страницы:
 * 25м и 50м несравнимы (README «Модель страницы», CARDS.md §5).
 */

export type PoolFilter = 'all' | '25m' | '50m';

interface CardProps {
  title: string;
  subtitle: string;
  /** Число в бейдже и подпись к нему («24 RECORDS» / «64 BESTS»). */
  count: number;
  countLabel: string;
  pool: PoolFilter;
  onPool: (pool: PoolFilter) => void;
  /** Показывается вместо сетки, когда данных нет и загрузка кончилась. */
  emptyText: string;
  isEmpty: boolean;
  /**
   * true — тело рисует владелец сам (Season best: секции дисциплин со своими рядами).
   * По умолчанию карточка сама раскладывает детей в сетку плиток (Record wall).
   */
  plainBody?: boolean;
  children: React.ReactNode;
}

function ClubRecordCard({
  title, subtitle, count, countLabel, pool, onPool, emptyText, isEmpty, plainBody, children,
}: CardProps) {
  return (
    <section className="deep-card mb-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <div className="deep-card-title">{title}</div>
          <div className="deep-card-sub mt-1">{subtitle}</div>
        </div>

        <div className="flex items-center gap-3">
          <span className="deep-count-badge">
            <b>{count}</b> {countLabel}
          </span>
          <div className="deep-seg">
            {(['all', '25m', '50m'] as PoolFilter[]).map((p) => (
              <button
                key={p}
                type="button"
                className={pool === p ? 'active' : ''}
                onClick={() => onPool(p)}
              >
                {p === 'all' ? 'All' : p}
              </button>
            ))}
          </div>
        </div>
      </div>

      {isEmpty ? (
        <div className="mt-4 text-[13px] font-bold" style={{ color: 'var(--deep-text-mute)' }}>
          {emptyText}
        </div>
      ) : plainBody ? (
        <div className="mt-4">{children}</div>
      ) : (
        // Карточки стоят парой по половине ширины, поэтому в ряду две плитки, а не три.
        <div className="mt-4 grid grid-cols-1 gap-3 sm:grid-cols-2">{children}</div>
      )}
    </section>
  );
}

interface TileProps {
  gender: string;
  /** Верхняя строка: дисциплина (Season best) либо ступень рекорда (Record wall). */
  topLine: React.ReactNode;
  /** Цвет верхней строки — им Record wall отличает мировой рекорд от возрастного. */
  topTone?: string;
  /** Вторая строка мелким: дисциплина, если верхняя занята ступенью. */
  secondLine?: React.ReactNode;
  time: string;
  /** Имя пловца/держателя. */
  name: string;
  /** Подпись под именем: год или дата рекорда. */
  footnote: string;
  /** Если задан — плитка становится ссылкой (у официальных рекордов ссылки нет). */
  href?: string;
  /** Качество времени (И11). Для официальных рекордов — kind='record'. */
  quality?: SwimQuality | null;
}

function ClubRecordTile({ gender, topLine, topTone, secondLine, time, name, footnote, href, quality }: TileProps) {
  const isFemale = gender === 'female';
  const className = `deep-record-tile ${isFemale ? 'deep-record-tile--f' : 'deep-record-tile--m'}`;

  const body = (
    <>
      <div className="flex items-start justify-between gap-2">
        <div
          className="min-w-0 truncate text-[11px] font-extrabold"
          style={{ color: topTone ?? 'var(--deep-text-mute)' }}
        >
          {topLine}
        </div>
        <span
          className="shrink-0 text-[13px]"
          style={{ color: isFemale ? 'var(--deep-female)' : 'var(--deep-male)' }}
        >
          {isFemale ? '♀' : '♂'}
        </span>
      </div>

      {secondLine && (
        <div className="mt-1 truncate text-[11px] font-extrabold" style={{ color: 'var(--deep-text-mute)' }}>
          {secondLine}
        </div>
      )}

      <div
        className="mt-1 text-[22px] leading-none tabular-nums"
        style={{ fontFamily: 'var(--deep-font-display)', color: 'var(--deep-text)' }}
      >
        <UI_SwimTime time={time} quality={quality} />
      </div>

      <div className="mt-2 truncate text-[12px] font-bold" style={{ color: 'var(--deep-text)' }} title={name}>
        {name}
      </div>
      <div className="truncate text-[10.5px] font-bold" style={{ color: 'var(--deep-text-ghost)' }}>
        {footnote}
      </div>
    </>
  );

  return href ? (
    <a href={href} className={`${className} block no-underline`}>
      {body}
    </a>
  ) : (
    <div className={className}>{body}</div>
  );
}

export { ClubRecordCard, ClubRecordTile };
