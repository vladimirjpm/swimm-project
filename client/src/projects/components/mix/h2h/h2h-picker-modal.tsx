import React from 'react';
import { createPortal } from 'react-dom';
import './h2h.css';
import '../../deep/deep-theme.css';
import { useDeepThemeClass } from '../../deep/use-deep-theme-class';

/**
 * Окно выбора стороны сравнения — общее для H2H (пловцы) и `/records/compare` (страны).
 *
 * Почему окно, а не блок в потоке, как было: выбор стоял ПОД сравнением, и на табе
 * страницы пловца уезжал за десяток карточек заплывов — жест «сменить соперника» требовал
 * сперва доскроллить до него (замечание Влада 23.09.2026). Окно приходит туда, где человек
 * уже смотрит, и одинаково работает на телефоне и на десктопе.
 *
 * ⚠ Это НЕ поповер у карточки: такой вариант уже пробовали 01.09.2026, и его выдача
 * уезжала за нижний край страницы — до последних имён нельзя было доскроллить. У окна
 * свой скролл и своя высота, эта ловушка к нему не относится.
 *
 * ⚠ Окно уходит ПОРТАЛОМ в `body`, снаружи корня страницы, поэтому класс темы ставит себе
 * само: за пределами корня роли `--t-*` и токены `--deep-*` пустые, и окно приехало бы
 * бесцветным (та же причина, что у модала логина).
 */
interface Props {
  open: boolean;
  /** Заголовок: какую сторону сейчас выбирают. */
  title: string;
  onClose: () => void;
  children: React.ReactNode;
}

const UI_H2HPickerModal: React.FC<Props> = ({ open, title, onClose, children }) => {
  const deep = useDeepThemeClass();
  const boxRef = React.useRef<HTMLDivElement>(null);

  React.useEffect(() => {
    if (!open) return undefined;

    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose(); };
    window.addEventListener('keydown', onKey);

    /**
     * Пока окно открыто, плавающий переключатель темы прячется. У него `z-index: 120` —
     * сознательно выше всех оверлеев продукта, и прятать его обязана страница
     * (`docs/ui-components.md`). Страниц с этим окном три, и одна из них — таб внутри
     * общего каркаса, который своей кнопкой не распоряжается; поэтому флаг ставит само
     * окно, а не каждый вызывающий по отдельности.
     */
    document.body.classList.add('h2h-modal-open');

    // Фокус в поиск: окно открыли, чтобы печатать, а не чтобы смотреть на него.
    const input = boxRef.current?.querySelector('input');
    input?.focus();

    return () => {
      window.removeEventListener('keydown', onKey);
      document.body.classList.remove('h2h-modal-open');
    };
  }, [open, onClose]);

  if (!open) return null;

  return createPortal(
    <div
      className={`${deep} h2h-modal`}
      // Закрытие по фону — на `mousedown` цели-фона: `click` срабатывал бы и тогда, когда
      // кнопку нажали внутри окна, а отпустили на фоне.
      onMouseDown={(e) => { if (e.target === e.currentTarget) onClose(); }}
    >
      <div className="h2h-modal__box" role="dialog" aria-modal="true" aria-label={title} ref={boxRef}>
        <div className="h2h-modal__head">
          <h2 className="h2h-modal__title">{title}</h2>
          <button type="button" className="h2h-modal__close" title="Close" onClick={onClose}>✕</button>
        </div>
        {children}
      </div>
    </div>,
    document.body,
  );
};

export default UI_H2HPickerModal;
