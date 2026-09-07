import React from 'react';
import { createPortal } from 'react-dom';
import './mobile-filters-drawer.css';

/**
 * МОБИЛЬНАЯ ШТОРКА ФИЛЬТРОВ — одна на продукт (Ф2 плана
 * `docs/plans/my-media-filters-plan.md`).
 *
 * До этого шторок было три, все свои: `MobileFiltersDrawer` внутри `results-main-project.tsx`,
 * нижняя шторка фильтров в `my-media.tsx` и шторка действий там же. Общего drawer в проекте
 * не было вовсе — поэтому «взять готовое» здесь означало вынести одну из них, а не написать
 * четвёртую.
 *
 * Компонент отвечает за оболочку: портал, затемнение, выезд, прокрутку и подвал. ЧТО внутри —
 * дело страницы, она передаёт свою панель детьми.
 *
 * Кнопки-триггера здесь НЕТ намеренно: у results это плавающая пилюля «Filters / Apply» поверх
 * таблицы, у личного кабинета — обычная кнопка в потоке страницы. Это хром страницы, и
 * навязывать один вид обеим — ровно та ошибка, из-за которой шторок стало три.
 */

interface Props {
  open: boolean;
  onClose: () => void;
  /**
   * `fullscreen` — шторка во весь экран (results: фильтров много, и они длинные).
   * `sheet` — нижняя шторка по содержимому, с ручкой и подвалом.
   */
  variant?: 'fullscreen' | 'sheet';
  /** Прилипший к низу подвал варианта `sheet` — обычно кнопка «Show N swims». */
  footer?: React.ReactNode;
  /** Класс панели: переопределение токенов `--fdrawer-*` и прочая палитра страницы. */
  className?: string;
  /** `id` панели — чтобы кнопка-триггер страницы сослалась на неё в `aria-controls`. */
  id?: string;
  children: React.ReactNode;
}

function MobileFiltersDrawer({
  open,
  onClose,
  variant = 'sheet',
  footer,
  className,
  id,
  children,
}: Props) {
  const node = (
    <div className={`fdrawer fdrawer--${variant}`}>
      <div
        className={`fdrawer__scrim${open ? ' fdrawer__scrim--open' : ''}`}
        onClick={onClose}
        aria-hidden="true"
      />
      <div
        id={id}
        className={`fdrawer__panel${open ? ' fdrawer__panel--open' : ''}${
          className ? ` ${className}` : ''
        }`}
        aria-hidden={!open}
      >
        {variant === 'sheet' && <div className="fdrawer__handle" />}
        <div className="fdrawer__body">{children}</div>
        {footer && <div className="fdrawer__footer">{footer}</div>}
      </div>
    </div>
  );

  return <>{createPortal(node, document.body)}</>;
}

export default MobileFiltersDrawer;
