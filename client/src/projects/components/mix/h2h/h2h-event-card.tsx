import React from 'react';
import './h2h.css';
import UI_SwimmStyleIcon from '../swimm-style-icon/swimm-style-icon';

/**
 * Карточка одного заплыва (макет 1b, §3): в шапке — иконка стиля между двумя hairline,
 * ниже полоса на каждый бассейн (`UI_H2HPoolRow`, приезжают через `children`).
 *
 * Текстового названия стиля и дистанции НЕТ — их несёт сама иконка (дистанция напечатана
 * на ней). Белая подложка под иконкой обязательна и не зависит от темы: PNG стилей
 * нарисованы под светлый фон и в тёмной без неё пропадают.
 *
 * `oneSided` — заплыв, который плавал только один из двоих: пунктир, приглушение,
 * серая иконка. Сравнивать там нечего, и карточка обязана выглядеть иначе.
 */
interface Props {
  stroke?: string | null;
  distance: string;
  oneSided?: boolean;
  children: React.ReactNode;
}

const UI_H2HEventCard: React.FC<Props> = ({ stroke, distance, oneSided = false, children }) => (
  <div className={`h2h-event${oneSided ? ' h2h-event--one-sided' : ''}`}>
    <div className="h2h-event__head">
      <div className="h2h-event__hairline" />
      <div className="h2h-event__icon">
        <UI_SwimmStyleIcon
          styleName={stroke ?? ''}
          styleLen={distance}
          styleType="icon-len"
        />
      </div>
      <div className="h2h-event__hairline" />
    </div>
    {children}
  </div>
);

export default UI_H2HEventCard;
