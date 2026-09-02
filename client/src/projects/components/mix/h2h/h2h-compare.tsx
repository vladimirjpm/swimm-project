import React from 'react';
import './h2h.css';
import UI_H2HMiniCard from './h2h-mini-card';
import UI_H2HEmptySlot from './h2h-empty-slot';
import UI_H2HCompareHeader from './h2h-compare-header';
import UI_H2HEventCard from './h2h-event-card';
import UI_H2HPoolRow from './h2h-pool-row';
import UI_H2HDivider from './h2h-divider';
import UI_H2HSwap from './h2h-swap';
import type { H2HSlot } from './h2h.types';
import type { SwimmerCompare, SwimmerCompareSwim } from '../../../swimmer-project/use-swimmer-page';

/**
 * ВЕСЬ экран сравнения двух пловцов (head-to-head), макет 1b из
 * `!design_handoff/design_handoff_h2h/`. Один компонент на два места: таб `?tab=h2h`
 * страницы пловца и отдельная страница `/h2h` (план — `docs/plans/h2h-page-plan.md`).
 *
 * Компонент НЕ знает, откуда его позвали: он видит два слота (занятый или пустой), цифры
 * сравнения и готовый блок выбора. Поэтому «левого менять нельзя» — это `onClear: null` у
 * слота, а не флаг варианта, и favorites приходят пропсами, а не из контекста: таб и
 * страница живут в разных провайдерах, а компонент из `mix/` не должен требовать чужого.
 *
 * Идентичность сторон (имя, клуб, возраст) берётся ИЗ СЛОТОВ, а цифры — из `compare`:
 * на странице пловцы известны раньше, чем приедет сравнение, и шапка не должна ждать.
 */
interface Props {
  left: H2HSlot;
  right: H2HSlot;
  /** null — сравнения нет: выбран не каждый, либо запрос ещё идёт. */
  compare: SwimmerCompare | null;
  state: { loading: boolean; error: boolean };
  /** Выбор пловца: у таба ищем соперника, у страницы — любую из сторон. */
  picker?: React.ReactNode;
  /** Текст пустого состояния, когда выбраны не оба. */
  emptyHint?: React.ReactNode;
  /**
   * Поменять стороны местами. Не задан — кнопки нет: в табе левый это хозяин профиля,
   * и «поменять местами» означало бы уехать с его страницы.
   */
  onSwap?: () => void;
}

/**
 * Подпись охвата для заголовка экрана: «best times of season 2025/26 · 5 comparable swims».
 * Живёт здесь, а не у вызывающего: «comparable swims» — это пары дистанция × бассейн, и
 * считать их второй раз снаружи значит завести второе определение той же цифры.
 */
export const h2hScopeLabel = (compare: SwimmerCompare): string => {
  const scope = compare.season == null ? 'career bests' : `best times of season ${compare.label}`;
  const shared = compare.sharedCount;
  return shared > 0
    ? `${scope} · ${shared} comparable swim${shared === 1 ? '' : 's'}`
    : `${scope} · nothing they both swam in the same pool`;
};

/** Бейдж строки: рекорд весомее места среди сверстников, поэтому REC перебивает SB. */
const badgeOf = (swim: SwimmerCompareSwim): 'SB' | 'REC' | null => {
  if (swim.holdsRecord) return 'REC';
  return swim.isSeasonBest ? 'SB' : null;
};

/** Ссылка на протокол заплыва — тот же адрес, что открывают строки остальных табов. */
const swimHref = (swim: SwimmerCompareSwim | null | undefined, swimmerId: number) => {
  const competitionId = swim?.competition?.id;
  return competitionId != null
    ? `/results?competitionId=${competitionId}&tab=swims&swimmerId=${swimmerId}`
    : undefined;
};

/** Слот как элемент шапки: занятый — мини-карточка, пустой — пунктирный слот. */
function Slot({ slot, align }: { slot: H2HSlot; align: 'left' | 'right' }) {
  if (slot.kind === 'empty') {
    return <UI_H2HEmptySlot label={slot.label} onClick={slot.onPick} />;
  }
  return (
    <UI_H2HMiniCard
      swimmer={slot.swimmer}
      align={align}
      isFavorite={slot.isFavorite ?? null}
      onToggleFavorite={slot.onToggleFavorite}
      onClear={slot.onClear ?? null}
    />
  );
}

const UI_H2HCompare: React.FC<Props> = ({
  left, right, compare, state, picker, emptyHint, onSwap,
}) => {
  const bothPicked = left.kind === 'swimmer' && right.kind === 'swimmer';

  // Пока выбраны не оба — только слоты и выбор: сравнивать нечего, и шапка со статами
  // показывала бы нули там, где данных попросту нет.
  if (!bothPicked) {
    return (
      <div className="h2h-scope">
        <div className="h2h-row h2h-row--slots">
          <Slot slot={left} align="left" />
          <div className="h2h-vs">
            vs
            <UI_H2HSwap onSwap={onSwap} />
          </div>
          <Slot slot={right} align="right" />
        </div>
        {picker}
      </div>
    );
  }

  if (state.error) {
    return (
      <div className="h2h-scope">
        <div className="h2h-empty">Could not load this comparison.</div>
        {picker}
      </div>
    );
  }

  if (!compare) {
    return (
      <div className="h2h-scope">
        <div className="h2h-empty">{state.loading ? 'Loading…' : emptyHint ?? 'No comparison yet.'}</div>
        {picker}
      </div>
    );
  }

  const both = compare.rows.filter((r) => r.pools.some((p) => p.deltaMs != null));
  const oneSided = compare.rows.filter((r) => !r.pools.some((p) => p.deltaMs != null));

  const eventCard = (row: SwimmerCompare['rows'][number], isOneSided: boolean) => (
    <UI_H2HEventCard
      key={row.key}
      stroke={row.stroke}
      distance={row.distance}
      oneSided={isOneSided}
    >
      {row.pools.map((pool) => (
        <UI_H2HPoolRow
          key={pool.poolType}
          poolType={pool.poolType}
          left={pool.mine ? {
            time: pool.mine.time,
            date: pool.mine.date,
            quality: pool.mine.quality,
            badge: badgeOf(pool.mine),
          } : null}
          right={pool.rival ? {
            time: pool.rival.time,
            date: pool.rival.date,
            quality: pool.rival.quality,
            badge: badgeOf(pool.rival),
          } : null}
          deltaMs={pool.deltaMs}
          // Ссылка ведёт в протокол того из двоих, чей это заплыв: у общей пары берём
          // левого — он же владелец экрана в табе.
          href={swimHref(pool.mine ?? pool.rival, pool.mine ? compare.mine.id : compare.rival.id)}
        />
      ))}
    </UI_H2HEventCard>
  );

  return (
    <div className="h2h-scope">
      <UI_H2HCompareHeader
        left={{
          swimmer: left.swimmer,
          seasonBests: compare.mine.seasonBests,
          medals: compare.mine.medals,
          bestPoints: compare.mine.bestPoints,
          recordsHeld: compare.mine.recordsHeld,
          isFavorite: left.isFavorite ?? null,
          onToggleFavorite: left.onToggleFavorite,
          onClear: left.onClear ?? null,
        }}
        right={{
          swimmer: right.swimmer,
          seasonBests: compare.rival.seasonBests,
          medals: compare.rival.medals,
          bestPoints: compare.rival.bestPoints,
          recordsHeld: compare.rival.recordsHeld,
          isFavorite: right.isFavorite ?? null,
          onToggleFavorite: right.onToggleFavorite,
          onClear: right.onClear ?? null,
        }}
        leftFaster={compare.mineFaster}
        rightFaster={compare.rivalFaster}
        ties={compare.ties}
        // В режиме ∞ места посчитаны за витринный сезон — говорим это прямо в подписи
        // строки, иначе цифра выглядит как «первых мест за всю карьеру».
        seasonBestsLabel={compare.season == null && compare.seasonBestLabel
          ? `season bests · ${compare.seasonBestLabel}`
          : 'season bests'}
        onSwap={onSwap}
      />

      {compare.rows.length === 0 ? (
        <div className="h2h-empty">Neither of them has a counted swim in this period.</div>
      ) : (
        <div className="h2h-events">
          {both.map((row) => eventCard(row, false))}
          {oneSided.length > 0 && <UI_H2HDivider text="only one swimmer" />}
          {oneSided.map((row) => eventCard(row, true))}
        </div>
      )}

      <div className="h2h-legend">
        One card per distance, one line per pool: the best time each of them swam there. Short
        course and long course are never compared with each other — a 25m time is faster by the
        pool alone. The gap is the left swimmer minus the right one, so a negative number means
        the left is faster. SB marks the fastest time among swimmers born the same year; REC
        means the time is not slower than the national record of that age. Relays, DSQ and
        flagged swims are left out.
      </div>

      {picker}
    </div>
  );
};

export default UI_H2HCompare;
