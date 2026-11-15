import React, { useEffect, useRef } from 'react';
import { createPortal } from 'react-dom';
import { dockHeight } from '../../../../components/deep/dock-slot';
import { useDeepThemeClass } from '../../../../components/deep/use-deep-theme-class';
import type { RsvpAnswer } from '../../../types';
import { useTrainingsTab } from '../trainings-context';
import { answerToastLabel } from './roll-format';

/**
 * Тост «<имя> → ✓ Going · Undo» после правки ответа тренером (Т6, хендофф Trainings Tab, 2a / 3b);
 * он же — «<имя> → lane 2 · Undo» после переноса в редакторе дорожек (CF3 потока тренера, `label`).
 * Живёт 5 с; новое изменение (другой `id`) заменяет прежний и перезапускает отсчёт. Состояние
 * тоста держит `roll-call.tsx`, здесь только вид и таймер.
 *
 * Портал в `body` со своим классом темы (снаружи страницы `--t-*` / `--deep-*` пустые). Инверсия
 * темы: фон `--deep-text`, текст `--deep-page-bg`. `z-[110]` — выше панели каркаса (40), ниже шторок
 * (125). На телефоне встаёт НАД нижней панелью каркаса «COACH TOOLS», на десктопе — снизу слева.
 */

const TOAST_MS = 5000;

export interface RollToastData {
  /** Растёт с каждым изменением — по нему сбрасывается таймер. */
  id: number;
  name: string;
  answer: RsvpAnswer | null;
  /** Свой текст после стрелки вместо подписи ответа — «lane 2» у переноса в редакторе дорожек (CF3). */
  label?: string;
  /** Слова сразу после имени, до стрелки — «is going» у ответа, поставившего на дорожку (CF4). */
  lead?: string;
}

function RollToast({
  toast, onUndo, onExpire,
}: {
  toast: RollToastData | null;
  onUndo: () => void;
  onExpire: () => void;
}) {
  const themeClass = useDeepThemeClass();
  const { isDesktop } = useTrainingsTab();
  // Свежий `onExpire` для таймера: эффект ставится на `toast.id`, а не на каждый рендер.
  const expire = useRef(onExpire);
  expire.current = onExpire;

  const id = toast?.id ?? null;
  useEffect(() => {
    if (id == null) return undefined;
    const timer = window.setTimeout(() => expire.current(), TOAST_MS);
    return () => window.clearTimeout(timer);
  }, [id]);

  if (!toast) return null;

  // Телефон: над нижней панелью каркаса, какой бы высоты она сейчас ни была (в ней бывает строка
  // статуса плана — dock-slot.ts); панели нет — прежний отступ.
  const dock = isDesktop ? 0 : dockHeight();
  const position: React.CSSProperties = isDesktop
    ? { left: 20, bottom: 20, width: 420 }
    : { left: 12, right: 12, bottom: dock > 0 ? dock + 10 : 'calc(96px + env(safe-area-inset-bottom))' };

  return createPortal(
    <div className={`fixed z-[110] ${themeClass}`} style={position}>
      <div
        role="status"
        aria-live="polite"
        className="flex min-h-[48px] items-center gap-2.5 text-[12.5px] font-bold"
        style={{ borderRadius: 12, padding: '0 6px 0 14px', background: 'var(--deep-text)', color: 'var(--deep-page-bg)' }}
      >
        <span className="min-w-0 flex-1 truncate">
          <bdi>{toast.name}</bdi>{toast.lead ? ` ${toast.lead}` : ''} → {toast.label ?? answerToastLabel(toast.answer)}
        </span>
        <button
          type="button"
          onClick={onUndo}
          className="h-9 flex-none cursor-pointer bg-transparent px-3 text-[12.5px] font-extrabold"
          style={{ borderRadius: 8, border: '1px solid var(--deep-page-bg)', color: 'inherit' }}
        >
          Undo
        </button>
      </div>
    </div>,
    document.body,
  );
}

export default RollToast;
