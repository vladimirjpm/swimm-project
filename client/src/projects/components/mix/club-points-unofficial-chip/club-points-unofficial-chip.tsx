import React from 'react';

/**
 * Чип «Unofficial» у клубных очков: официального зачёта у соревнования нет, а очки
 * посчитаны НАМИ по шкале правила (обычно автоподбор — шкала чемпионата Израиля). По духу
 * Р76 (docs/data-integrity.md): цифры протокола показываем как есть, а наш вывод из них
 * подписываем. Решение — Р80; когда флаг true, решает сервер (`Competition.IsClubPointsUnofficial`).
 *
 * Один компонент на все места, где видны клубные очки соревнования: карточка Top clubs, таб
 * Clubs, фильтр Club в табе Swims, карточка Standings и история страницы клуба. Подпись и
 * пояснение обязаны совпадать — иначе экраны про одно соревнование скажут разное.
 *
 * Тем-нейтрален: на deep-страницах (клуб) красится ролями `--t-*`, без них — токенами
 * режима `--theme-mode-*` (страница результатов).
 */

export const UNOFFICIAL_POINTS_TITLE =
  'This competition has no official club standings. The points are our own estimate, '
  + 'scored from the official places.';

interface Props {
  className?: string;
  /** `sm` — строка списка (история клуба), по умолчанию — заголовок карточки. */
  size?: 'md' | 'sm';
}

const UI_ClubPointsUnofficialChip: React.FC<Props> = ({ className = '', size = 'md' }) => {
  const muted = 'var(--t-text-3, var(--theme-mode-text-muted))';
  return (
    <span
      className={`inline-block whitespace-nowrap rounded-full font-extrabold ${
        size === 'sm' ? 'px-1.5 py-px text-[9.5px]' : 'px-2 py-[2px] text-[11px]'
      } ${className}`}
      style={{
        background: `color-mix(in srgb, ${muted} 12%, transparent)`,
        color: 'var(--t-text-2, var(--theme-mode-text-secondary))',
        border: `1px solid color-mix(in srgb, ${muted} 30%, transparent)`,
      }}
      title={UNOFFICIAL_POINTS_TITLE}
      aria-label={`Unofficial. ${UNOFFICIAL_POINTS_TITLE}`}
    >
      Unofficial
    </span>
  );
};

export default UI_ClubPointsUnofficialChip;
