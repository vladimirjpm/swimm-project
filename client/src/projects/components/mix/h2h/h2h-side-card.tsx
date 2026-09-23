import React from 'react';
import './h2h.css';

/**
 * Сторона сравнения в шапке head-to-head — ОДНА на весь продукт: пловец в H2H и страна на
 * `/records/compare` (решение Влада 23.09.2026). Раньше это были два похожих куска вёрстки,
 * и правка в одном тихо расходилась с другим.
 *
 * Компонент знает только фигуру: медиа (портрет или флаг), имя, подпись, чип, кнопка во
 * внешнем углу и строка-ссылка ПОД карточкой. Чем именно сторона является — его не
 * касается; портрет и флаг приезжают готовыми узлами.
 *
 * ⚠ **Сама карточка — кнопка «выбрать другого», а не ссылка.** Так пришли оба экрана: у
 * страны клик занять было нечем, а у пловца ссылка на профиль отъела единственный жест и
 * заставила завести ✕ в углу, который плохо читался поверх фото. Теперь наоборот: клик =
 * сменить сторону (главное действие экрана сравнения), а профиль/рекорды — отдельной
 * строкой под карточкой, где ссылка выглядит ссылкой.
 *
 * `onSelect: null` — сторону сменить нельзя (в табе H2H левый это хозяин страницы): тогда
 * карточка не кнопка вовсе, а обычный блок. Это ДАННЫЕ, а не режим экрана.
 */
interface Props {
  align: 'left' | 'right';
  /** Портрет пловца либо флаг страны — готовым узлом; размеры задаёт вызывающий. */
  media: React.ReactNode;
  name: React.ReactNode;
  /** Вторая строка: клуб пловца, число рекордов страны. */
  sub?: React.ReactNode;
  /** Чип под подписью («9 y · 2017»). */
  chip?: React.ReactNode;
  /** Кнопка во ВНЕШНЕМ углу — сердечко избранного; у страны её нет. */
  corner?: React.ReactNode;
  /** Клик по карточке: выбрать другую сторону. null — сторона несменяема. */
  onSelect?: (() => void) | null;
  /** Подсказка и `aria-label` кнопки: карточка обязана назвать своё действие словами. */
  selectHint?: string;
  /** Эту сторону заполнит следующий выбор в пикере: акцентная рамка-предупреждение. */
  active?: boolean;
  /** Строка под карточкой: профиль пловца, рекорды страны. */
  link?: { href: string; label: React.ReactNode };
  /** Класс на саму карточку — для экранных отличий (`rc-nation`). */
  className?: string;
}

const UI_H2HSideCard: React.FC<Props> = ({
  align, media, name, sub, chip, corner, onSelect = null, selectHint, active = false, link,
  className = '',
}) => {
  const text = (
    <span className="h2h-mini__text">
      <span dir="auto" className="h2h-mini__name">{name}</span>
      {sub && <span dir="auto" className="h2h-mini__club">{sub}</span>}
      {chip && <span className="h2h-mini__age">{chip}</span>}
    </span>
  );

  // Зеркальность: у левой стороны текст идёт первым, чтобы медиа смотрела В ЦЕНТР шапки.
  const body = (
    <>
      {corner}
      {align === 'left' ? <>{text}{media}</> : <>{media}{text}</>}
    </>
  );

  const classes = `h2h-mini h2h-mini--${align}${active ? ' h2h-mini--active' : ''}`
    + `${onSelect ? '' : ' h2h-mini--static'}${className ? ` ${className}` : ''}`;

  return (
    <div className={`h2h-side h2h-side--${align}`}>
      {onSelect ? (
        <button type="button" className={classes} title={selectHint} aria-label={selectHint} onClick={onSelect}>
          {body}
        </button>
      ) : (
        <div className={classes}>{body}</div>
      )}
      {link && (
        <a className="h2h-side__link" href={link.href}>{link.label}</a>
      )}
    </div>
  );
};

export default UI_H2HSideCard;
