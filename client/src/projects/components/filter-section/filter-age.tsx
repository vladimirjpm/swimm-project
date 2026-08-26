import React, { useRef, useCallback } from 'react';
import { useFilterHost } from './filter-host';
import FilterCard from './filter-card';

/**
 * Возраст. Что означают кнопки — решает хост (Ф3): у results это ГОДЫ РОЖДЕНИЯ, у мастерсов
 * готовые возрастные ГРУППЫ протокола («25-29»), у страницы с сезонным возрастом будет
 * просто возраст. Отсюда и разный заголовок карточки.
 *
 * Диапазон долгим нажатием осмыслен только для годов рождения: «с 2013 по 2015» читается,
 * «с группы 25-29 по группу 35-39» — нет.
 */
const FilterAge: React.FC = () => {
  const { values, set, options } = useFilterHost();
  const ages = options.ages;

  if (!ages) return null;

  const age = values.age ?? 'all';
  const ageTo = values.age_to;
  const canRange = ages.mode === 'birth-year';
  const isRange = canRange && !!ageTo && age !== 'all';

  const summary =
    age === 'all' ? 'All' : isRange ? `${age}–${ageTo}` : String(age);

  return (
    <FilterCard
      // Заголовок несёт смысл оси: группы протокола, сезонный возраст или год рождения.
      title={
        ages.mode === 'age-group'
          ? 'Age Group'
          : ages.mode === 'age'
            ? 'Age in season'
            : 'Age'
      }
      summary={summary}
      isActive={age !== 'all'}
    >
      {canRange && (
        <div className="text-[11px] text-[var(--theme-mode-text-muted)] mb-2">
          multi-select with long-press
          {isRange && (
            <span className="ml-2 font-semibold text-[var(--theme-primary)]">
              birth year: {age}–{ageTo}
            </span>
          )}
        </div>
      )}
      <div className="flex flex-wrap gap-2">
        {ages.values.map((by) => {
          const isInRange =
            canRange &&
            age !== 'all' &&
            !!ageTo &&
            by !== 'all' &&
            Number(by) >= Number(age) &&
            Number(by) <= Number(ageTo);
          const isActive = age === by && !ageTo;

          // Подпись под годом рождения: сколько лет человеку на стартах этой выборки.
          // Осенние и весенние старты одного сезона дают разный возраст — отсюда вилка.
          let ageLabel = '';
          if (by !== 'all' && ages.compYears.length > 0) {
            const list = ages.compYears.map((cy) => cy - Number(by));
            const minAge = Math.min(...list);
            const maxAge = Math.max(...list);
            ageLabel = minAge === maxAge ? `${minAge}` : `${minAge}-${maxAge}`;
          }

          return (
            <AgeButton
              key={by}
              age={by}
              ageLabel={ageLabel}
              isActive={!!isActive}
              isInRange={isInRange}
              onClick={() => set({ age: by, age_to: undefined })}
              onLongPress={() => {
                // Только по годам рождения: Number('25-29') это NaN, и диапазон из групп
                // записал бы в фильтр строку «NaN».
                if (canRange && age !== 'all' && by !== 'all' && age !== by) {
                  const from = Math.min(Number(age), Number(by));
                  const to = Math.max(Number(age), Number(by));
                  set({ age: String(from), age_to: String(to) });
                }
              }}
            />
          );
        })}
      </div>

      {/* Вторая шкала: другая система координат, поэтому под чертой и со своей подписью,
          а не вперемешку с основным рядом. */}
      {ages.extra && ages.extra.values.length > 0 && (
        <div className="mt-3 pt-3 border-t border-dashed border-[var(--theme-mode-border-input)]">
          <div className="text-[11px] text-[var(--theme-mode-text-muted)] mb-2">
            {ages.extra.title}
          </div>
          <div className="flex flex-wrap gap-2">
            {ages.extra.values.map((value) => (
              <button
                key={value}
                className={`fseg ${age === value ? 'fseg-active' : ''}`}
                onClick={() => set({ age: value, age_to: undefined })}
              >
                {value}
              </button>
            ))}
          </div>
        </div>
      )}
    </FilterCard>
  );
};

export default FilterAge;

/* ─── AgeButton with long-press support ─── */

function AgeButton({
  age,
  ageLabel,
  isActive,
  isInRange,
  onClick,
  onLongPress,
}: {
  age: string;
  ageLabel: string;
  isActive: boolean;
  isInRange: boolean;
  onClick: () => void;
  onLongPress: () => void;
}) {
  const timerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const didLongPress = useRef(false);

  const startPress = useCallback(() => {
    didLongPress.current = false;
    timerRef.current = setTimeout(() => {
      didLongPress.current = true;
      onLongPress();
    }, 500);
  }, [onLongPress]);

  const endPress = useCallback(() => {
    if (timerRef.current) {
      clearTimeout(timerRef.current);
      timerRef.current = null;
    }
    if (!didLongPress.current) {
      onClick();
    }
  }, [onClick]);

  const cancelPress = useCallback(() => {
    if (timerRef.current) {
      clearTimeout(timerRef.current);
      timerRef.current = null;
    }
  }, []);

  const cls = isActive
    ? 'fseg-active'
    : isInRange
      ? 'fseg-active opacity-75'
      : '';

  return (
    <button
      className={`fseg ${cls}`}
      onMouseDown={startPress}
      onMouseUp={endPress}
      onMouseLeave={cancelPress}
      onTouchStart={startPress}
      onTouchEnd={endPress}
      onTouchCancel={cancelPress}
      onContextMenu={(e) => e.preventDefault()}
    >
      {age === 'all' ? (
        'all'
      ) : (
        <span className="flex flex-col">
          {age}
          {ageLabel && (
            <span className="text-xs opacity-70 ml-0.5">
              ({ageLabel})
            </span>
          )}
        </span>
      )}
    </button>
  );
}
