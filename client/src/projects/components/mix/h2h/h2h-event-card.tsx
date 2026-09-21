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
 *
 * `variant="record"` — та же карточка в золоте: слева рекорд пловца, справа мировой
 * рекорд мастерс той же ступени (страница пловца, «Official records»). Сетка, шапка и
 * полосы бассейнов те же — отличается только семья цветов и выравнивание сторон.
 */
interface Props {
  stroke?: string | null;
  distance: string;
  oneSided?: boolean;
  variant?: 'h2h' | 'record';
  /**
   * Шапка карточки. По умолчанию — иконка стиля с дистанцией. `false` убирает шапку целиком:
   * так карточка стоит на `/records?tab=masters`, где стиль и дистанция ОБЩИЕ на всю
   * страницу и названы в полосе фильтров — повторять их над каждой карточкой незачем
   * (решение Влада 20.09.2026). Свой узел заменяет иконку, оставляя hairline на месте.
   */
  head?: React.ReactNode | false;
  children: React.ReactNode;
}

const UI_H2HEventCard: React.FC<Props> = ({
  stroke, distance, oneSided = false, variant = 'h2h', head, children,
}) => (
  <div className={`h2h-event${oneSided ? ' h2h-event--one-sided' : ''}${variant === 'record' ? ' h2h-event--record' : ''}`}>
    {head !== false && (
    <div className="h2h-event__head">
      <div className="h2h-event__hairline" />
      {head ?? (
      <div className="h2h-event__icon">
        <UI_SwimmStyleIcon
          styleName={stroke ?? ''}
          styleLen={distance}
          styleType="icon-len"
          // Дистанция стоит ПОД стилем в ОБОИХ вариантах (просьба Влада 20–21.09.2026):
          // числом поверх рисунка она спорит с ним, а здесь у карточки широкая белая плита
          // и место под подпись есть. На остальных экранах продукта умолчание компонента
          // (`overlay`) не меняется — там плитки узкие.
          lenPlacement="below"
          // Крупнее унаследованного кегля (18.75px): под иконкой число стоит само по себе,
          // и мелким читалось плохо. 30px подобрано глазами: вдвое (38) упиралось в нижний
          // край плиты на телефоне.
          lenSize={30}
          className="src-h2h-event-card"
        />
      </div>
      )}
      <div className="h2h-event__hairline" />
    </div>
    )}
    {children}
  </div>
);

export default UI_H2HEventCard;
