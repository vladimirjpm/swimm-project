import React from 'react';
import {
  showcaseNoticeKind,
  showcaseNoticeText,
  type ShowcaseSeasonNotice,
} from '../../../../utils/helpers/season-helper';

/**
 * Одна фраза на все витрины season best: «новый сезон уже идёт, но лучшие времена откроются
 * после зимнего чемпионата» (правило — docs/season-boundary-rule.md).
 *
 * ⚠ Зачем это вообще: с 1 сентября и до конца февраля календарный сезон и витринный
 * расходятся. Без заметки страница либо молча показывает прошлый сезон (и цифры выглядят
 * протухшими), либо — если новый сезон выбран руками — отдаёт пустоту, неотличимую от
 * поломки. Первым это поймал сам Влад 01.09.2026 на `/clubs/438?tab=records`.
 *
 * Текст берётся из `showcaseNoticeText` — он же уходит в тултип плитки шапки клуба, где
 * разметка невозможна. Сервер отдаёт только данные (`season_notice`); формулировка одна на
 * все пять экранов: клуб (карточка + плитка), страница результатов, `/season-best`, пловец.
 *
 * Оболочки у экранов разные, поэтому класс задаёт вызывающий: `deep-scope-note` там, где
 * подключена deep-тема, свой — там, где её нет.
 */
function UI_SeasonNotice({
  notice,
  season,
  show = 'both',
  className = 'deep-scope-note',
}: {
  notice: ShowcaseSeasonNotice | null | undefined;
  /** Сезон, на котором стоит витрина. null — сезон выбрала она сама. */
  season?: number | null;
  /**
   * `both` (по умолчанию) — витрина сама выбирает сезон, и оговорка нужна в обоих случаях:
   * и «показываем прошлый», и «в новом пока пусто».
   *
   * `pending` — сезон витрине ДИКТУЕТ контекст, а не умолчание (страница результатов берёт
   * сезон открытого соревнования). Объяснять там «показываем прошлый сезон» неверно: его
   * никто не подставлял, его выбрал пользователь, открыв этот протокол. Осмысленна только
   * вторая ветка — «в новом сезоне данных ещё нет».
   */
  show?: 'both' | 'pending';
  className?: string;
}) {
  if (show === 'pending' && showcaseNoticeKind(notice, season) !== 'pending') return null;

  const text = showcaseNoticeText(notice, season);
  if (!text) return null;

  return <div className={className}>{text}</div>;
}

export default UI_SeasonNotice;
