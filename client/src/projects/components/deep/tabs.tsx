import React from 'react';

/**
 * Плитки-табы «папка» (design_handoff_club_page_tabs, вариант 3a folder-tab).
 *
 * ОБЩИЙ компонент страниц сущности: клуба, группы и спортсмена. Хендофф страницы
 * спортсмена требует те же плитки «1:1 со страницы клуба» — копия разъехалась бы на
 * первой же правке стыка плитки с панелью, а стык тут нетривиальный (-1px и z-index).
 *
 * Подписи-сводки — ЖИВЫЕ данные (правило хендоффа: не хардкод). Там, где числа у
 * страницы ещё нет, в подписи стоит слово, а не выдуманная цифра.
 *
 * Роли (хендофф group-club-changes §3): фиолетовый = «ты можешь это менять». Его видит только
 * тот, у кого есть права, и всегда вместе со знаком ✎ или подписью, а не одним цветом.
 * - `editable` — обычный таб, где у зрителя есть правка (Team: уровни, Media: галерея):
 *   фиолетовый текст и ✎ в углу.
 * - `pinned` — инструменты управляющего (Trainings, Admin). На десктопе они в конце ряда
 *   за пунктиром с подписью `toolsLabel`; на телефоне — закреплённой панелью у низа экрана,
 *   а верхний ряд остаётся коротким (5 колонок вместо 7 — иначе колонка ~45px).
 *
 * Иконки декоративные (aria-hidden), имя таба — текст; тап-таргет в мобайле ≥48px
 * задан в deep-theme.css (.deep-tab).
 */

export interface DeepTabItem<T extends string> {
  id: T;
  /** Глиф-иконка; декоративная (aria-hidden). */
  icon: string;
  label: string;
  /** Короткое имя для мобайла: колонка там узкая, и длинное обрезается многоточием. */
  shortLabel?: string;
  /** Подпись-сводка под именем (только десктоп). */
  sub?: string;
  /** Зритель может здесь править — фиолетовый + ✎. */
  editable?: boolean;
  /** Содержимое закрыто замком — на телефоне (где нет подписи) плитка несёт 🔒. */
  locked?: boolean;
  /** Инструмент управляющего: конец ряда (десктоп) / нижняя панель (телефон). */
  pinned?: boolean;
  /** Короткая метка у инструмента: «3 new» — ждущие заявки. Только у `pinned`. */
  badge?: string;
}

interface Props<T extends string> {
  tabs: DeepTabItem<T>[];
  active: T;
  onSelect: (tab: T) => void;
  /** Название набора для скринридера («Club sections», «Athlete sections»). */
  ariaLabel: string;
  /** Подпись группы инструментов («Coach tools», «Club admin»). */
  toolsLabel?: string;
}

function DeepTabs<T extends string>({ tabs, active, onSelect, ariaLabel, toolsLabel = 'Tools' }: Props<T>) {
  const main = tabs.filter((t) => !t.pinned);
  const tools = tabs.filter((t) => t.pinned);

  return (
    <div className="deep-tabs-row" role="tablist" aria-label={ariaLabel}>
      <div
        className="deep-tabs"
        // Число колонок задаётся данными: у клуба 6 табов, у спортсмена 6, у тренера 5.
        style={{ ['--deep-tabs-count' as string]: main.length }}
      >
        {main.map((tab) => (
          <button
            key={tab.id}
            type="button"
            role="tab"
            aria-selected={active === tab.id}
            onClick={() => onSelect(tab.id)}
            className={[
              'deep-tab',
              active === tab.id ? 'deep-tab--active' : '',
              tab.editable ? 'deep-tab--edit' : '',
            ].filter(Boolean).join(' ')}
          >
            <span className="deep-tab__head">
              <span className="deep-tab__icon" aria-hidden="true">{tab.icon}</span>
              <span className={`deep-tab__name${tab.shortLabel ? ' max-sm:hidden' : ''}`}>
                {tab.label}
              </span>
              {tab.shortLabel && (
                <span className="deep-tab__name sm:hidden" aria-hidden="true">{tab.shortLabel}</span>
              )}
              {tab.editable && (
                <span className="deep-tab__mark" title="You can edit this">✎</span>
              )}
              {!tab.editable && tab.locked && (
                <span className="deep-tab__mark sm:hidden" aria-hidden="true">🔒</span>
              )}
            </span>
            {/* Подпись только на десктопе — в мобайле колонка узкая (макет 3a) */}
            <span className="deep-tab__sub max-sm:hidden">{tab.sub ?? ''}</span>
          </button>
        ))}
      </div>

      {tools.length > 0 && (
        <div className="deep-tabs-tools">
          <span className="deep-tabs-tools__label">{toolsLabel}</span>
          <div className="deep-tabs-tools__row">
            {tools.map((tab) => (
              <button
                key={tab.id}
                type="button"
                role="tab"
                aria-selected={active === tab.id}
                onClick={() => onSelect(tab.id)}
                title={tab.sub}
                className={`deep-tool-tab${active === tab.id ? ' deep-tool-tab--active' : ''}`}
              >
                <span className="deep-tool-tab__icon" aria-hidden="true">{tab.icon}</span>
                <span className="deep-tool-tab__name">{tab.label}</span>
                {tab.badge && <span className="deep-tool-tab__badge">{tab.badge}</span>}
              </button>
            ))}
          </div>
        </div>
      )}
    </div>
  );
}

export default DeepTabs;
