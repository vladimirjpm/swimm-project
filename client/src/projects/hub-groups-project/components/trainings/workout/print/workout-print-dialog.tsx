import React, { useEffect, useLayoutEffect, useMemo, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { useDeepThemeClass } from '../../../../../components/deep/use-deep-theme-class';
import { DEFAULT_PRINT_VIEW, PRINT_VIEWS } from './print-views';
import { availableViews, pickView } from './print-view-pick';
import { previewScale, sheetSize } from './print-preview-scale';
import type { PrintSheetFit, WorkoutPrintSource, WorkoutPrintView } from './print-types';
import { usePrintSession } from './use-print-session';

/** Зазор между листами в предпросмотре, px листа (до масштаба). При печати его нет. */
const PAGE_GAP = 24;

/**
 * Окно «Print preview» (docs/plans/workout-print-plan.md §2 Р1, §4): листы вида в масштабе под экран
 * и кнопки `Print` / `Close`. Рисуется порталом в `#wprint-root` (прямой ребёнок `<body>`, его даёт
 * `usePrintSession`); при печати остаются только листы — всё с `.wprint-noprint` прячется, масштаб и
 * позиционирование предков снимает `workout-print.css`. Закрыли (Close, Esc) — окно размонтировано,
 * страница такая же, как была.
 *
 * От вида окно знает только контракт `WorkoutPrintView`: годные виды (`available`) — сегментом, когда
 * их больше одного; листов у вида сколько угодно — стопкой (масштаб по одному листу, дальше прокрутка);
 * `duplex` — подсказка включить двустороннюю печать. `views` — реестр (по умолчанию `PRINT_VIEWS`;
 * стенд ui-kit подставляет свой).
 */
export function WorkoutPrintDialog({
  source,
  onClose,
  views = PRINT_VIEWS,
  defaultView = DEFAULT_PRINT_VIEW,
}: {
  source: WorkoutPrintSource;
  onClose: () => void;
  views?: Record<string, WorkoutPrintView>;
  defaultView?: string;
}) {
  const themeClass = useDeepThemeClass();
  const list = useMemo(() => availableViews(views, source), [views, source]);
  const [viewId, setViewId] = useState<string>(defaultView);
  const view = pickView(list, viewId);
  const orientation = view?.orientation ?? 'portrait';
  const { root, print } = usePrintSession(orientation);
  const [status, setStatus] = useState<PrintSheetFit | null>(null);
  const stageRef = useRef<HTMLDivElement>(null);
  const pagesRef = useRef<HTMLDivElement>(null);
  const printRef = useRef<HTMLButtonElement>(null);
  const [avail, setAvail] = useState({ w: 0, h: 0 });
  const [pages, setPages] = useState({ count: 1, height: 0 });

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

  // Область под листы: замер и подписка на смену (поворот телефона, ресайз). `root` появляется после
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

  // Стопка листов вида: сколько их и какой высоты стопка ДО масштаба (`offsetHeight` масштаб не видит) —
  // по ней рамка под уменьшенную стопку. Листов вид может нарисовать сколько угодно и не сразу.
  useLayoutEffect(() => {
    const el = pagesRef.current;
    if (!el) return undefined;
    const measure = () => {
      const count = el.querySelectorAll(':scope > .wprint-sheet').length;
      setPages((p) => (p.count === count && p.height === el.offsetHeight ? p : { count, height: el.offsetHeight }));
    };
    measure();
    const ro = new ResizeObserver(measure);
    ro.observe(el);
    const mo = new MutationObserver(measure);
    mo.observe(el, { childList: true });
    return () => { ro.disconnect(); mo.disconnect(); };
  }, [root, view?.id]);

  useEffect(() => { if (root) printRef.current?.focus(); }, [root]);

  if (!root) return null;

  const k = previewScale(avail.w, avail.h, orientation);
  const size = sheetSize(orientation);
  const stackHeight = pages.height || size.height;
  const choose = (id: string) => {
    if (id === view?.id) return;
    setStatus(null);
    setViewId(id);
  };

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
          {(status?.caption || pages.count > 1) && (
            <div className="hp-mono truncate text-[11px] font-bold" style={{ color: 'var(--t-text-3)' }}>
              {[status?.caption, pages.count > 1 ? `${pages.count} pages` : null].filter(Boolean).join(' · ')}
            </div>
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
          disabled={!view}
          className="h-10 cursor-pointer rounded-[10px] border-0 px-5 text-[13px] font-extrabold disabled:cursor-default disabled:opacity-50"
          style={{ background: 'var(--deep-seg-active-bg)', color: 'var(--deep-seg-active-fg)' }}
        >
          Print
        </button>
        {list.length > 1 && (
          <div className="deep-seg basis-full sm:basis-auto" role="group" aria-label="Print view">
            {list.map((v) => (
              <button
                key={v.id}
                type="button"
                className={v.id === view?.id ? 'active' : ''}
                aria-pressed={v.id === view?.id}
                onClick={() => choose(v.id)}
              >
                {v.label}
              </button>
            ))}
          </div>
        )}
        {view?.duplex && pages.count > 1 && (
          <p className="m-0 basis-full text-[12px] font-bold" style={{ color: 'var(--t-text-2)' }}>
            {pages.count} pages — turn on “Print on both sides” in the print dialog to get one sheet of paper
          </p>
        )}
        {status?.overflow && (
          <p role="alert" className="m-0 basis-full text-[12px] font-bold" style={{ color: 'var(--t-warn)' }}>
            Too long for one page — the bottom is cut off
          </p>
        )}
      </div>

      <div ref={stageRef} className="wprint-stage min-h-0 flex-1 overflow-auto p-3.5" style={{ background: 'var(--deep-seg-track)' }}>
        <div className="mx-auto" style={{ width: size.width * k, height: stackHeight * k }}>
          <div
            ref={pagesRef}
            className="wprint-pages"
            style={{ width: size.width, display: 'flex', flexDirection: 'column', gap: PAGE_GAP, transform: `scale(${k})`, transformOrigin: 'top left' }}
          >
            {/* `key` — сменили вид: новый лист с нуля, подгонка прошлого вида не мешает. */}
            {view && <view.Sheet key={view.id} source={source} onFit={setStatus} />}
          </div>
        </div>
        {!view && <p className="m-0 text-center text-[13px] font-bold">Nothing to print for this workout</p>}
      </div>
    </div>,
    root,
  );
}
