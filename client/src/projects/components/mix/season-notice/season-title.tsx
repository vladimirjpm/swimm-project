import React from 'react';
import {
  showcaseNoticeKind,
  showcaseNoticeText,
  type ShowcaseSeasonNotice,
} from '../../../../utils/helpers/season-helper';

/**
 * Заголовок «Season 2025/26» над витриной сезонного рейтинга (Season best, места среди
 * ровесников), пока она держит ПРОШЛЫЙ сезон: календарь уже в новом, а рейтинги нового
 * откроются после зимнего чемпионата (решение Влада 26.10.2026, docs/season-boundary-rule.md).
 *
 * Видимая подпись, а не только плашка-объяснение: без неё прошлогодние места читались как
 * сегодняшние. Полная фраза — та же `showcaseNoticeText`, что у `UI_SeasonNotice`, — уходит в
 * подсказку (`title`), формулировка одна на продукт.
 *
 * Рисуется ТОЛЬКО когда витрина показывает прошлый сезон (`holding`); после зимнего чемпионата
 * сезоны совпадают, и заголовок пропадает сам.
 *
 * `size="lg"` — для экрана, где рядом стоит ДРУГОЙ сезон (карусель пловца на 2026/27, а места —
 * за 2025/26): там заголовок обязан быть строкой в шапке, а не мелким чипом.
 */
function UI_SeasonTitle({
  notice,
  season,
  size = 'sm',
  className = '',
}: {
  notice: ShowcaseSeasonNotice | null | undefined;
  /** Сезон, на котором стоит витрина. null — сезон выбрала она сама (это и есть показываемый). */
  season?: number | null;
  size?: 'sm' | 'lg';
  className?: string;
}) {
  if (!notice || showcaseNoticeKind(notice, season) !== 'holding') return null;
  const hint = showcaseNoticeText(notice, season) ?? undefined;

  return size === 'lg' ? (
    <div className={`deep-season-title ${className}`} title={hint}>
      <span className="deep-season-title__label">Season {notice.showing_label}</span>
      <span className="deep-season-title__hint">
        {notice.pending_label} season bests open after the winter championships
      </span>
    </div>
  ) : (
    <span className={`deep-season-chip ${className}`} title={hint}>
      Season {notice.showing_label}
    </span>
  );
}

export default UI_SeasonTitle;
