import React from 'react';
import './season-best-badge.css';

/**
 * `UI_SeasonBestBadge` — плашка «SB»: это время лучшее в стране за сезон на своей ступени
 * (пол × возраст в сезоне × дисциплина × бассейн).
 *
 * Отдельный компонент, а не вариант `UI_RecordBadge`: рекорд — факт справочника, живущий
 * годами, SB — положение внутри одного сезона, которое чужой заплыв может отобрать завтра.
 * Смешивать их в одном компоненте значит рано или поздно показать одно вместо другого.
 *
 * Палитра — заливное золото с тёмным текстом (как медальные кружки и чип SB в `UI_SwimRow`):
 * один и тот же «физический объект» в обеих темах, обводка золотом по золотому давала
 * контраст ниже 4.5 при 10px bold.
 */
interface Props {
  /** Подпись возрастной ступени для тултипа: «14y», «23y». Пусто — общий текст. */
  scope?: string | null;
  className?: string;
}

const UI_SeasonBestBadge: React.FC<Props> = ({ scope, className }) => (
  <span
    className={`sb-badge${className ? ` ${className}` : ''}`}
    title={scope ? `Fastest in the country this season (${scope})` : 'Fastest in the country this season'}
  >
    SB
  </span>
);

export default UI_SeasonBestBadge;
