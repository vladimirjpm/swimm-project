import React, { useEffect, useLayoutEffect, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { useDeepThemeClass } from '../../../../../components/deep/use-deep-theme-class';
import { DEFAULT_PRINT_VIEW, PRINT_VIEWS } from './print-views';
import { previewScale, sheetSize } from './print-preview-scale';
import type { PrintSheetFit, WorkoutPrintSource } from './print-types';
import { usePrintSession } from './use-print-session';

/**
 * Окно «Print preview» (docs/plans/workout-print-plan.md §2 Р1): лист A4 в масштабе под экран и
 * кнопки `Print` / `Close`. Рисуется порталом в `#wprint-root` (прямой ребёнок `<body>`, его даёт
 * `usePrintSession`); при печати остаётся один лист — всё с `.wprint-noprint` прячется, масштаб и
 * позиционирование предков снимает `workout-print.css`. Закрыли (Close, Esc) — окно размонтировано,
 * страница такая же, как была. Вид пока один (`DEFAULT_PRINT_VIEW`); сегмент `View` — когда видов станет два.
 */
export function WorkoutPrintDialog({ source, onClose }: { source: WorkoutPrintSource; onClose: () => void }) {
  const themeClass = useDeepThemeClass();
  const view = PRINT_VIEWS[DEFAULT_PRINT_VIEW];
  const { root, print } = usePrintSession(view.orientation);
  const [status, setStatus] = useState<PrintSheetFit | null>(null);
  const stageRef = useRef<HTMLDivElement>(null);
  const printRef = useRef<HTMLButtonElement>(null);
  const [avail, setAvail] = useState({ w: 0, h: 0 });

  // Вернуть фокус на кнопку, откуда открыли; страницу под окном не прокручивать.
  useEffect(() => {
    const opener = document.activeElement as HTMLElement | null;
    const prev = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    return () => {
      document.body.style.overflow = prev;
      opener?.focus?.();
    };
  }, []);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose(); };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [onClose]);

  // Область под лист: замер и подписка на смену (поворот телефона, ресайз). `root` появляется после
  // первого эффекта — область ждёт его.
  useLayoutEffect(() => {
    const el = stageRef.current;
    if (!el) return undefined;
    const measure = () => {
      const cs = getComputedStyle(el);
      const padX = parseFloat(cs.paddingLeft) + parseFloat(cs.paddingRight);
      const padY = parseFloat(cs.paddingTop) + parseFloat(cs.paddingBottom);
      setAvail({ w: el.clientWidth - padX, h: el.clientHeight - padY });
    };
    measure();
    const ro = new ResizeObserver(measure);
    ro.observe(el);
    return () => ro.disconnect();
  }, [root]);

  useEffect(() => { if (root) printRef.current?.focus(); }, [root]);

  if (!root) return null;

  const k = previewScale(avail.w, avail.h, view.orientation);
  const size = sheetSize(view.orientation);
  const Sheet = view.Sheet;

  return createPortal(
    <div
      className={`${themeClass} fixed inset-0 z-[130] flex flex-col`}
      style={{ background: 'var(--deep-page-bg)', color: 'var(--deep-text)' }}
      role="dialog"
      aria-modal="true"
      aria-label="Print preview"
    >
      <div
        className="wprint-noprint flex shrink-0 flex-wrap items-center gap-x-3 gap-y-1.5 px-3.5 pb-2.5"
        style={{ paddingTop: 'calc(12px + env(safe-area-inset-top))', borderBottom: '1px solid var(--deep-divider)' }}
      >
        <div className="min-w-0 flex-1">
          <div className="text-[14px] font-extrabold">Print preview</div>
          {status?.caption && (
            <div className="hp-mono truncate text-[11px] font-bold" style={{ color: 'var(--t-text-3)' }}>{status.caption}</div>
          )}
        </div>
        <button
          type="button"
          onClick={onClose}
          className="h-10 cursor-pointer rounded-[10px] border px-4 text-[13px] font-extrabold"
          style={{ borderColor: 'var(--deep-card-border)', background: 'var(--deep-card-bg)', color: 'var(--deep-text)' }}
        >
          Close
        </button>
        <button
          ref={printRef}
          type="button"
          onClick={print}
          className="h-10 cursor-pointer rounded-[10px] border-0 px-5 text-[13px] font-extrabold"
          style={{ background: 'var(--deep-seg-active-bg)', color: 'var(--deep-seg-active-fg)' }}
        >
          Print
        </button>
        {status?.overflow && (
          <p role="alert" className="m-0 basis-full text-[12px] font-bold" style={{ color: 'var(--t-warn)' }}>
            Too long for one page — the bottom is cut off
          </p>
        )}
      </div>

      <div ref={stageRef} className="wprint-stage min-h-0 flex-1 overflow-auto p-3.5" style={{ background: 'var(--deep-seg-track)' }}>
        <div className="mx-auto" style={{ width: size.width * k, height: size.height * k, boxShadow: '0 2px 18px rgba(0,0,0,.35)' }}>
          <div style={{ width: size.width, height: size.height, transform: `scale(${k})`, transformOrigin: 'top left' }}>
            {/* `onFit` — итог подгонки вида: подпись раскладки и кегля, «не влезло» (план §3.5). */}
            <Sheet source={source} onFit={setStatus} />
          </div>
        </div>
      </div>
    </div>,
    root,
  );
}
