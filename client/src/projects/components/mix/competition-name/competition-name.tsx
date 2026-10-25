import React from 'react';

/** Роль старта в сезоне (`kind` из API): winter | summer | openwater. */
const KIND_ICON: Record<string, { icon: string; label: string }> = {
  winter: { icon: '❄', label: 'Winter championship' },
  summer: { icon: '☀', label: 'Summer championship' },
  openwater: { icon: '🌊', label: 'Open water' },
};

export interface UI_CompetitionNameProps {
  name?: string | null;
  /** Чемпионат — 🏆 перед названием. Источник — ручной флаг `Competition.IsChampionship` из API. */
  isChampionship?: boolean | null;
  /** Роль в сезоне (winter | summer | openwater) — значок ❄/☀/🌊 после 🏆. Не задано — без значка. */
  kind?: string | null;
  /** Классы на само название (кегль, цвет, обрезка) — типографика за вызывающим экраном. */
  className?: string;
  /** Обёртка: `span` по умолчанию; `div`, когда название стоит отдельной строкой. */
  as?: 'span' | 'div';
}

/**
 * Название соревнования — ЕДИНСТВЕННЫЙ способ его показать (решение Влада 25.10.2026).
 *
 * Раньше 🏆 у чемпионата дописывал каждый экран сам, и половина экранов о нём забывала:
 * таб Overview страницы пловца показывал «אליפות ישראל …» без кубка, хотя данные его несли.
 * Компонент держит всё, что у названия есть: кубок чемпионата, значок роли в сезоне,
 * `dir="auto"` (названия ивритские) и полное название в подсказке — длинные обрезаются.
 *
 * Флаг — из данных, не из имени: «אליפות» в названии не делает старт чемпионатом
 * (вето «готовимся к чемпионату»), а ручная пометка в админке — делает.
 *
 * ⚠ Обрезку многоточием (`truncate`) ставь НА САМ компонент (`className`, при нужде
 * `as="div"`), а не на обёртку. У компонента `dir="auto"`, поэтому для обёртки он изолирован
 * и в определении направления не участвует: LTR-обёртка срезает НАЧАЛО ивритского названия
 * вместе с 🏆 (поймано 25.10.2026 на Recent meets). Разделитель «·» — отдельным элементом рядом.
 */
const UI_CompetitionName: React.FC<UI_CompetitionNameProps> = ({
  name,
  isChampionship = false,
  kind = null,
  className = '',
  as = 'span',
}) => {
  if (!name) return null;
  const kindIcon = kind ? KIND_ICON[kind] : undefined;
  const Tag = as;
  const title = [isChampionship ? 'Championship' : null, kindIcon?.label, name].filter(Boolean).join(' · ');

  return (
    <Tag dir="auto" className={className} title={title}>
      {isChampionship && <span aria-label="Championship" role="img">🏆 </span>}
      {kindIcon && <span aria-label={kindIcon.label} role="img">{kindIcon.icon} </span>}
      {name}
    </Tag>
  );
};

export default UI_CompetitionName;
