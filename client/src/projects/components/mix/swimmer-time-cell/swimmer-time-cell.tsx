import React, { useState } from 'react';
import '../text-effect/text-effect.css';
import UI_SwimTime, { SwimQuality, SwimTimeDelta } from '../swim-time/swim-time';
import UI_RecordBadge, { type RecordKind } from '../record-badge/record-badge';

const TIME_SPLIT_SEPARATOR = '›';

interface UI_SwimmerTimeCellProps {
  time: string;
  time_split: string;
  time_fail: boolean;
  time_fail_note: string | null;
  firstLineClassName?: string;
  secondLineClassName?: string;
  className?: string;
  /** Это время БЬЁТ действующий рекорд (не «пловец держит рекорд» — см. UI_SwimmerNameCell). */
  isRecordHolder?: boolean;
  /**
   * Класс рекорда для бейджа. Мастерский старт меряется мастерской полосой, обычный —
   * возрастной ступенью: справочник у них разный, и одинаковым бейджем они выглядели бы
   * одним достижением.
   */
  recordKind?: RecordKind;
  /** Ступень рекорда для подсказки: «14» у возрастного, «45-49» у мастерского. */
  recordScope?: string | null;
  /**
   * Качество времени (docs/plans/swim-time-quality-everywhere-plan.md). null — всё в порядке.
   * Значок и объяснение рисует `UI_SwimTime` — единственный шов вывода времени.
   */
  quality?: SwimQuality | null;
  /**
   * Как показать спорное время: 'icon' — значок ⚠ рядом с цифрами (по умолчанию),
   * 'chip' — подпись «⚠ Under review» ПОД временем (строка таблицы результатов,
   * гибрид 15d). В chip-режиме ячейка становится колонкой: время сверху, чип снизу —
   * иначе подпись не влезает в узкую колонку времени и ломает сетку строки.
   */
  qualityMarker?: 'icon' | 'chip';
  /** Отставание от лидера в мс — рисует `UI_SwimTime` сразу за временем. */
  gapMs?: number | null;
  /** Остальные сравнения с эталонами (Δ клуб, Δ Израиль) — строками под временем. */
  deltas?: SwimTimeDelta[];
  /** Кегль и цвет строк сравнения. */
  gapClassName?: string;
}

const UI_SwimmerTimeCell: React.FC<UI_SwimmerTimeCellProps> = ({
  time,
  time_split,
  time_fail,
  time_fail_note,
  firstLineClassName = 'text-xl font-bold',
  secondLineClassName = 'text-xs',
  className = '',
  isRecordHolder = false,
  recordKind = 'age',
  recordScope,
  quality = null,
  qualityMarker = 'icon',
  gapMs = null,
  deltas,
  gapClassName = '',
}) => {
  const [splitOpen, setSplitOpen] = useState(false);

  // Чип встаёт ПОД время (колонкой), значок — в строку с цифрами. Раскладку меняем только
  // когда чип реально есть: у обычных строк ячейка времени остаётся ровно такой, как была.
  const stacked = qualityMarker === 'chip' && !!quality;
  const stackedClass = stacked ? 'flex flex-col items-center gap-[3px]' : '';

  const formattedTimeSplit = time_split
    ? time_split
        .split(';')
        .map((s) => s.trim())
        .filter(Boolean)
        .join(` ${TIME_SPLIT_SEPARATOR} `)
    : '';

  const handleToggle = (e: React.MouseEvent) => {
    e.stopPropagation();
    setSplitOpen((prev) => !prev);
  };

  return (
    <div className={className}>
      {/* Рекорд, побитый ЭТИМ заплывом. Бейдж общий на весь продукт (UI_RecordBadge):
          три класса и одно правило цвета — золото только национальному. */}
      {isRecordHolder && (
        <div className="mb-1 flex justify-start">
          <UI_RecordBadge kind={recordKind} scope={recordScope} isNew />
        </div>
      )}
      {formattedTimeSplit ? (
        <div
          className={`${firstLineClassName} ${stacked ? stackedClass : 'flex items-center gap-1'} cursor-pointer select-none`}
          onClick={handleToggle}
        >
          <UI_SwimTime
            time={time}
            quality={quality}
            marker={qualityMarker}
            chipSize="sm"
            gapMs={gapMs}
            deltas={deltas}
            gapClassName={gapClassName}
          />
          {time_fail && <span className="text-red-500 ml-1">*</span>}
          <span
            className={`theme-text-muted transition-transform duration-200 ${splitOpen ? 'rotate-180' : ''}`}
            style={{ fontSize: '10px', lineHeight: 1 }}
          >➗</span>
        </div>
      ) : (
        <div className={`${firstLineClassName} ${stackedClass}`}>
          <UI_SwimTime
            time={time}
            quality={quality}
            marker={qualityMarker}
            chipSize="sm"
            gapMs={gapMs}
            deltas={deltas}
            gapClassName={gapClassName}
          />
          {time_fail && <span className="text-red-500 ml-1">*</span>}
        </div>
      )}
      {time_fail_note && (
        <div className={`text-red-500 ${secondLineClassName}`}>{time_fail_note}</div>
      )}
      {formattedTimeSplit && splitOpen && (
        <div className={`theme-text-muted flex whitespace-nowrap ${secondLineClassName}`}>{formattedTimeSplit}</div>
      )}
    </div>
  );
};

export default UI_SwimmerTimeCell;
