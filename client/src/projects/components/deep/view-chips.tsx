import React from 'react';

/**
 * Чипы вида внутри таба: «≡ Results · 🏅 Records 20 · ☀ Season bests» и подпись под ними.
 *
 * Жили внутри страницы пловца (`ResultsFilters`, swimmer-panels.tsx). Хендофф
 * group-club-changes §3 сводит Records и Season bests клуба и группы в ОДИН таб Results
 * «ровно как у пловца» — поэтому форма общая, а набор чипов у каждой страницы свой.
 *
 * Чипы переносятся по строкам и никогда не скроллятся: на 375px ни один не уходит за край.
 * Стили — `.deep-filter-*` в deep-theme.css.
 */

export interface DeepViewChip<T extends string> {
  id: T;
  icon: string;
  label: string;
  /** Число в кольце справа (рекордов 20). null/0 — без бейджа. */
  badge?: number | null;
  /** Подпись под рядом, когда чип выбран: что именно показано. */
  caption: string;
}

function DeepViewChips<T extends string>({
  chips, active, onSelect, ariaLabel,
}: {
  chips: DeepViewChip<T>[];
  active: T;
  onSelect: (id: T) => void;
  /** Имя группы для скринридера («Results view»). */
  ariaLabel: string;
}) {
  const current = chips.find((c) => c.id === active) ?? chips[0];

  return (
    <div className="deep-filters">
      <div className="deep-filter-row" role="group" aria-label={ariaLabel}>
        {chips.map((c) => (
          <button
            key={c.id}
            type="button"
            onClick={() => onSelect(c.id)}
            aria-pressed={active === c.id}
            className={`deep-filter-chip${active === c.id ? ' deep-filter-chip--active' : ''}`}
          >
            <span aria-hidden="true">{c.icon}</span>
            <span className="deep-filter-chip__label">{c.label}</span>
            {c.badge != null && c.badge > 0 && (
              <span className="deep-filter-chip__badge">{c.badge}</span>
            )}
          </button>
        ))}
      </div>
      {current && <div className="deep-filter-caption">{current.caption}</div>}
    </div>
  );
}

/**
 * Вид внутри таба живёт в `?view=` (как у пловца): диплинк на «Records группы» работает сразу.
 * Вид по умолчанию — без параметра; replaceState, чтобы «назад» не ходил по чипам.
 */
function readViewParam<T extends string>(allowed: readonly T[], fallback: T): T {
  const v = new URLSearchParams(window.location.search).get('view') as T | null;
  return v != null && allowed.includes(v) ? v : fallback;
}

function writeViewParam(view: string, fallback: string): void {
  const url = new URL(window.location.href);
  if (view === fallback) url.searchParams.delete('view');
  else url.searchParams.set('view', view);
  window.history.replaceState(null, '', url.toString());
}

/**
 * Легаси-адреса табов: `?tab=records` и `?tab=lanes` стали видами внутри Results и
 * Trainings. Переписываем адрес ДО того, как каркас прочтёт `?tab=` (вызывать из
 * инициализатора состояния страницы), — старая ссылка открывает то же, что открывала.
 */
function rewriteLegacyTab(aliases: Record<string, { tab: string; view?: string }>): void {
  const url = new URL(window.location.href);
  const alias = aliases[url.searchParams.get('tab') ?? ''];
  if (!alias) return;
  url.searchParams.set('tab', alias.tab);
  if (alias.view) url.searchParams.set('view', alias.view);
  window.history.replaceState(null, '', url.toString());
}

export default DeepViewChips;
export { readViewParam, writeViewParam, rewriteLegacyTab };
