import React, { useState } from 'react';
import type { TrainingSessionList, TrainingSessionSets, TrainingType } from '../../../types';
import { useTrainingsTab } from '../trainings-context';
import TrainingsEmpty from '../trainings-empty';
import { trainingTypeOf } from '../training-types';
import FeedFilters, { type FeedFilter } from './feed-filters';
import { monthKey } from './feed-format';
import TrainingCard from './training-card';

/**
 * Лента тренировок вида Trainings (прототип 2a / 3a / 3b): фильтр, записи с разделителями
 * месяцев, «Older trainings ↓», одна раскрытая карточка «Sets & times» с ленивой загрузкой.
 * Данные — только из контекста таба; своих `fetch` нет. Состояние ленты (фильтр, сколько
 * показано, какая карточка раскрыта, кэш сетов) живёт здесь и в адрес не пишется.
 */

/** Сколько записей видно сразу и сколько добавляет «Older trainings ↓». */
const PAGE = 6;

/** Кнопка «+ New training» — одна, у управляющего; на телефоне её может забрать нижняя панель (Т6). */
export function NewTrainingButton({ height }: { height: number }) {
  const { openSheet } = useTrainingsTab();
  return (
    <button
      type="button"
      onClick={() => openSheet({ kind: 'new-training' })}
      className="cursor-pointer whitespace-nowrap border px-3 text-[12.5px] font-extrabold"
      style={{
        height,
        borderRadius: 10,
        borderColor: 'var(--deep-ow-border)',
        background: 'var(--deep-ow-chip)',
        color: 'var(--deep-ow)',
      }}
    >
      + New training
    </button>
  );
}

function TrainingFeed({ data }: { data: TrainingSessionList }) {
  const { manages, isDesktop, sessions } = useTrainingsTab();
  const [filter, setFilter] = useState<FeedFilter>('all');
  /** Тип тренировки из второго ряда чипов; null — все типы. */
  const [typeFilter, setTypeFilter] = useState<TrainingType | null>(null);
  const [visible, setVisible] = useState(PAGE);
  const [expanded, setExpanded] = useState<number | null>(null);
  // Кэш сетов по тренировке: нет записи — ещё грузится, null — не удалось.
  const [setsCache, setSetsCache] = useState<Map<number, TrainingSessionSets | null>>(() => new Map());

  const all = data.sessions;
  // Числа типов — по всей ленте; числа первого ряда — внутри выбранного типа (что увидишь, нажав).
  const typeCounts: Partial<Record<TrainingType, number>> = {};
  all.forEach((s) => { const t = trainingTypeOf(s).id; typeCounts[t] = (typeCounts[t] ?? 0) + 1; });
  // Выбранный тип пропал из ленты (удалили тренировку) — фильтр по нему не держим.
  const activeType = typeFilter && typeCounts[typeFilter] ? typeFilter : null;
  const ofType = activeType ? all.filter((s) => trainingTypeOf(s).id === activeType) : all;
  const counts = {
    all: ofType.length,
    times: ofType.filter((s) => s.has_results).length,
    videos: ofType.filter((s) => s.media.length > 0).length,
  };
  const filtered = filter === 'times' ? ofType.filter((s) => s.has_results)
    : filter === 'videos' ? ofType.filter((s) => s.media.length > 0)
      : ofType;
  const shown = filtered.slice(0, visible);

  const changeFilter = (next: FeedFilter) => {
    setFilter(next);
    setVisible(PAGE);
    setExpanded(null);
  };

  const changeType = (next: TrainingType | null) => {
    setTypeFilter(next);
    setVisible(PAGE);
    setExpanded(null);
  };

  const storeSets = (id: number, value: TrainingSessionSets | null) =>
    setSetsCache((prev) => new Map(prev).set(id, value));

  const toggleSets = (id: number) => {
    if (expanded === id) { setExpanded(null); return; }
    setExpanded(id);
    // Первое раскрытие (или повтор после ошибки) — грузим в обработчике, не в эффекте.
    if (!setsCache.get(id)) {
      setSetsCache((prev) => { const next = new Map(prev); next.delete(id); return next; });
      sessions.loadSets(id).then((loaded) => storeSets(id, loaded));
    }
  };

  const toggleKey = async (id: number, setNo: number | null): Promise<string | null> => {
    const error = await sessions.setKeySet(id, setNo);
    if (error) return error;
    // Список тренировок хук перечитает сам (и `my_key` в карточке); сеты этой тренировки — здесь.
    const fresh = await sessions.loadSets(id);
    if (fresh) storeSets(id, fresh);
    return null;
  };

  let emptyText: string | null = null;
  if (all.length > 0 && filtered.length === 0) {
    emptyText = filter === 'times' ? 'No trainings with times yet.' : 'No trainings with videos yet.';
  }

  return (
    <div>
      {/* ⚠ В Т6 кнопка может переехать в нижнюю панель тренера — потому отдельным блоком. */}
      {manages && !isDesktop && (
        <div className="mb-3 flex justify-end">
          <NewTrainingButton height={44} />
        </div>
      )}

      {all.length > 0 && (
        <FeedFilters
          value={filter}
          counts={counts}
          onChange={changeFilter}
          types={typeCounts}
          type={activeType}
          onType={changeType}
          coach={manages}
          isDesktop={isDesktop}
        />
      )}

      {all.length === 0 && (
        <TrainingsEmpty
          glyph="▶"
          title="No trainings yet"
          text={manages
            ? 'Add one — e.g. “Underwater filming” — and attach videos to it. Times aren’t needed.'
            : 'Trainings show up here after the coach adds one or imports times. Then you can attach videos and photos to them.'}
        />
      )}

      {emptyText && (
        <p className="m-0 text-[12.5px] font-bold" style={{ color: 'var(--t-text-3)' }}>{emptyText}</p>
      )}

      {shown.map((s, i) => (
        <TrainingCard
          key={s.id}
          session={s}
          canAddMedia={data.can_add_media}
          monthDivider={i === 0 || monthKey(shown[i - 1].date) !== monthKey(s.date)}
          expanded={expanded === s.id}
          onToggleSets={() => toggleSets(s.id)}
          sets={setsCache.get(s.id) ?? null}
          setsLoading={!setsCache.has(s.id)}
          onToggleKey={(setNo) => toggleKey(s.id, setNo)}
        />
      ))}

      {filtered.length > visible && (
        <button
          type="button"
          onClick={() => setVisible((v) => v + PAGE)}
          className="cursor-pointer border-0 bg-transparent p-0 text-[12px] font-extrabold"
          style={{ color: 'var(--deep-accent)', paddingLeft: (isDesktop ? 44 : 40) + (isDesktop ? 10 : 8) }}
        >
          Older trainings ↓
        </button>
      )}
    </div>
  );
}

export default TrainingFeed;
