import React, { useState } from 'react';
import UI_SwimTime from '../../../../components/mix/swim-time/swim-time';
import { levelColor } from '../../level-color';
import type { TrainingSet, TrainingSetRow, TrainingSessionSets } from '../../../types';
import { setSummary } from './feed-format';

/**
 * «Sets & times» — раскрывается ВНУТРИ карточки ленты (прототип 5a). Данные приходят готовыми
 * из ленты (`training-feed.tsx` грузит лениво и кэширует), здесь только вид: переключатель
 * `All / Me`, заголовки сетов, ★ ключевого сета у тренера, строки повторов.
 *
 * Время — только через `UI_SwimTime` (тренировочный замер, без `quality`).
 */

const ROW_GRID = '28px minmax(0,1fr) 62px 26px 14px';

function RowLine({ row, setNo }: { row: TrainingSetRow; setNo: number }) {
  const name = row.name || row.name_en;
  return (
    <div
      className="grid min-h-[32px] items-center gap-1.5 rounded-lg px-2"
      style={{
        gridTemplateColumns: ROW_GRID,
        background: row.mine ? 'var(--deep-accent-chip)' : 'var(--deep-card-bg-raised)',
      }}
    >
      <span className="hp-mono text-[10.5px] font-bold" style={{ color: 'var(--t-text-3)' }}>
        {setNo}.{row.order_no}
      </span>
      <span dir="auto" className="min-w-0 truncate text-left text-[12px] font-extrabold" style={{ color: 'var(--t-text)' }}>
        <bdi>{name}</bdi>
      </span>
      <span className="text-right" style={{ color: row.mine ? 'var(--deep-accent)' : 'var(--t-text)' }}>
        {row.time == null ? (
          <span className="hp-mono text-[12px] font-extrabold" style={{ color: 'var(--t-text-3)' }}>—</span>
        ) : (
          <UI_SwimTime time={row.time} className="hp-mono text-[12px] font-extrabold" />
        )}
      </span>
      <span className="hp-mono text-[10px] font-bold uppercase" style={{ color: 'var(--t-text-3)' }}>
        {row.intensity ? row.intensity.toUpperCase() : ''}
      </span>
      <span className="flex justify-end">
        {row.level && (
          <span
            title={row.level.name}
            className="inline-block h-2 w-2 rounded-full"
            style={{ background: levelColor(row.level) }}
          />
        )}
      </span>
    </div>
  );
}

function SetsAndTimes({
  data, loading, canManage, busy, onToggleKey,
}: {
  /** null и не `loading` — не удалось загрузить. */
  data: TrainingSessionSets | null;
  loading: boolean;
  /** Управляющий — может ставить ★ (флаг приходит с сервером в `data.can_manage`). */
  canManage: boolean;
  /** Идёт запись (общий `busy` хука) — кнопки ★ недоступны. */
  busy: boolean;
  /** Поставить / снять ★; null — успех, иначе текст отказа. */
  onToggleKey: (setNo: number | null) => Promise<string | null>;
}) {
  const [scope, setScope] = useState<'all' | 'me'>('all');
  const [keyError, setKeyError] = useState<string | null>(null);

  if (loading) {
    return <p className="m-0 text-[12px] font-bold" style={{ color: 'var(--t-text-3)' }}>Loading…</p>;
  }
  if (!data) {
    return <p className="m-0 text-[12px] font-bold" style={{ color: 'var(--deep-danger)' }} role="alert">Could not load the sets.</p>;
  }

  const hasMine = data.sets.some((s) => s.rows.some((r) => r.mine));
  const manage = canManage || data.can_manage;
  const shown: TrainingSet[] = scope === 'me'
    ? data.sets
      .map((s) => ({ ...s, rows: s.rows.filter((r) => r.mine) }))
      .filter((s) => s.rows.length > 0)
    : data.sets;

  const toggleKey = async (set: TrainingSet) => {
    setKeyError(null);
    const error = await onToggleKey(set.is_key ? null : set.set_no);
    if (error) setKeyError(error);
  };

  return (
    <div className="flex flex-col gap-1.5">
      {hasMine && (
        <div className="flex justify-end">
          <div
            className="inline-flex rounded-full p-0.5"
            style={{ background: 'var(--deep-seg-track)' }}
            role="group"
            aria-label="Show swimmers"
          >
            {(['all', 'me'] as const).map((id) => (
              <button
                key={id}
                type="button"
                aria-pressed={scope === id}
                onClick={() => setScope(id)}
                className="cursor-pointer rounded-full border-0 px-2.5 py-1 text-[11px] font-extrabold"
                style={scope === id
                  ? { background: 'var(--deep-seg-active-bg)', color: 'var(--deep-seg-active-fg)' }
                  : { background: 'transparent', color: 'var(--t-text-3)' }}
              >
                {id === 'all' ? 'All' : 'Me'}
              </button>
            ))}
          </div>
        </div>
      )}

      {keyError && (
        <p className="m-0 text-[12px] font-extrabold" style={{ color: 'var(--deep-danger)' }} role="alert">{keyError}</p>
      )}

      {shown.map((set) => (
        <div key={set.set_no} className="flex flex-col gap-1">
          <div className="flex items-center justify-between gap-2 pt-1">
            <span className="text-[10px] font-extrabold uppercase tracking-[.06em]" style={{ color: 'var(--t-text-3)' }}>
              Set {set.set_no}{setSummary(set)}
              {/* Тренеру значок ★ — сама кнопка справа; участнику — только метка. */}
              {set.is_key && !manage && <span style={{ color: 'var(--deep-gold)' }}> ★</span>}
            </span>
            {manage && (
              <button
                type="button"
                aria-pressed={set.is_key}
                disabled={busy}
                title={set.is_key
                  ? 'Key set — members see their best swim here as “You”'
                  : 'Mark as the key set'}
                onClick={() => toggleKey(set)}
                className="h-7 w-7 cursor-pointer border-0 bg-transparent text-[16px] leading-none disabled:cursor-default disabled:opacity-50"
                style={{ color: set.is_key ? 'var(--deep-gold)' : 'var(--t-text-3)' }}
              >
                {set.is_key ? '★' : '☆'}
              </button>
            )}
          </div>
          {set.rows.map((row, i) => (
            <RowLine key={`${row.swimmer_id}-${row.order_no}-${i}`} row={row} setNo={set.set_no} />
          ))}
        </div>
      ))}
    </div>
  );
}

export default SetsAndTimes;
