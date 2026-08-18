import React from 'react';

/**
 * Плитки-табы «папка» (design_handoff_club_page_tabs, вариант 3a folder-tab).
 *
 * ОБЩИЙ компонент двух страниц: клуба (5 табов) и спортсмена (6). Хендофф страницы
 * спортсмена требует те же плитки «1:1 со страницы клуба» — копия разъехалась бы на
 * первой же правке стыка плитки с панелью, а стык тут нетривиальный (-1px и z-index).
 *
 * Подписи-сводки — ЖИВЫЕ данные (правило хендоффа: не хардкод). Там, где числа у
 * страницы ещё нет, в подписи стоит слово, а не выдуманная цифра.
 *
 * Иконки декоративные (aria-hidden), имя таба — текст; тап-таргет в мобайле ≥48px
 * задан в deep-theme.css (.deep-tab).
 */

export interface DeepTabItem<T extends string> {
  id: T;
  /** Глиф-иконка; декоративная (aria-hidden). */
  icon: string;
  label: string;
  /** Короткое имя для мобайла: колонка там ~45px и длинное обрезается многоточием. */
  shortLabel?: string;
  /** Подпись-сводка под именем (только десктоп). */
  sub?: string;
}

interface Props<T extends string> {
  tabs: DeepTabItem<T>[];
  active: T;
  onSelect: (tab: T) => void;
  /** Название набора для скринридера («Club sections», «Athlete sections»). */
  ariaLabel: string;
}

function DeepTabs<T extends string>({ tabs, active, onSelect, ariaLabel }: Props<T>) {
  return (
    <div
      className="deep-tabs"
      role="tablist"
      aria-label={ariaLabel}
      // Число колонок задаётся данными: у клуба 5 табов, у спортсмена 6.
      style={{ ['--deep-tabs-count' as string]: tabs.length }}
    >
      {tabs.map((tab) => (
        <button
          key={tab.id}
          type="button"
          role="tab"
          aria-selected={active === tab.id}
          onClick={() => onSelect(tab.id)}
          className={`deep-tab${active === tab.id ? ' deep-tab--active' : ''}`}
        >
          <span className="deep-tab__head">
            <span className="deep-tab__icon" aria-hidden="true">{tab.icon}</span>
            <span className={`deep-tab__name${tab.shortLabel ? ' max-sm:hidden' : ''}`}>
              {tab.label}
            </span>
            {tab.shortLabel && (
              <span className="deep-tab__name sm:hidden" aria-hidden="true">{tab.shortLabel}</span>
            )}
          </span>
          {/* Подпись только на десктопе — в мобайле колонка узкая (макет 3a) */}
          <span className="deep-tab__sub max-sm:hidden">{tab.sub ?? ''}</span>
        </button>
      ))}
    </div>
  );
}

export default DeepTabs;
