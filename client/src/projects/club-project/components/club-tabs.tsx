import React from 'react';

/**
 * Табы страницы клуба (design_handoff_club_page_club_tabs, вариант 3a).
 *
 * Карточки страницы разложены по пяти табам: Season (фильтр сезонов + грид + зачёт),
 * Records, Swimmers, Media, History. Сами карточки при этом остались независимыми
 * блоками `.deep-card` — таб только решает, какие из них показать.
 *
 * Подписи-сводки — ЖИВЫЕ данные (правило хендоффа: не хардкод). Там, где числа у
 * страницы ещё нет (рекорды считает сама карточка, медиа не подключены), в подписи
 * стоит слово, а не выдуманная цифра.
 *
 * Иконки декоративные (aria-hidden), имя таба — текст; тап-таргет в мобайле ≥48px
 * задан в club-theme.css (.deep-tab).
 */

export type ClubTab = 'season' | 'records' | 'swimmers' | 'media' | 'history';

export const CLUB_TABS: ClubTab[] = ['season', 'records', 'swimmers', 'media', 'history'];

export function isClubTab(value: string | null | undefined): value is ClubTab {
  return value != null && (CLUB_TABS as string[]).includes(value);
}

const ICON: Record<ClubTab, string> = {
  season: '▦',
  records: '⏱',
  swimmers: '🏊',
  media: '▶',
  history: '🗓',
};

const LABEL: Record<ClubTab, string> = {
  season: 'Season',
  records: 'Records',
  swimmers: 'Swimmers',
  media: 'Media',
  history: 'History',
};

interface Props {
  active: ClubTab;
  onSelect: (tab: ClubTab) => void;
  /** Подписи-сводки под именем таба (десктоп); в мобайле скрыты. */
  subs: Record<ClubTab, string>;
}

function ClubTabs({ active, onSelect, subs }: Props) {
  return (
    <div className="deep-tabs" role="tablist" aria-label="Club sections">
      {CLUB_TABS.map((tab) => (
        <button
          key={tab}
          type="button"
          role="tab"
          aria-selected={active === tab}
          onClick={() => onSelect(tab)}
          className={`deep-tab${active === tab ? ' deep-tab--active' : ''}`}
        >
          <span className="deep-tab__head">
            <span className="deep-tab__icon" aria-hidden="true">{ICON[tab]}</span>
            <span className="deep-tab__name">{LABEL[tab]}</span>
          </span>
          {/* Подпись только на десктопе — в мобайле колонка узкая (макет 3a) */}
          <span className="deep-tab__sub max-sm:hidden">{subs[tab]}</span>
        </button>
      ))}
    </div>
  );
}

export default ClubTabs;
