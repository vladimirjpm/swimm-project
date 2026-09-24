import React, { useEffect, useId, useState } from 'react';
import { createPortal } from 'react-dom';
import { useDeepThemeClass } from '../deep/use-deep-theme-class';

/** Итог подтверждённого действия: неуспех показывается в диалоге, успех закрывает вызывающий. */
export interface ConfirmResult {
  success: boolean;
  error?: string;
}

/**
 * Общий диалог подтверждения опасного действия (docs/ui-components.md). Вынесен, когда
 * понадобился второй такой диалог (убрать из избранного на /my-favorites) — первый,
 * удаление группы, теперь тоже стоит на нём, копий нет.
 *
 * Что он держит сам, чтобы не переписывать в каждом месте:
 * - **портал в `body` + класс темы deep** на корне (`useDeepThemeClass`): снаружи страницы
 *   роли `--t-*` пустые;
 * - **Esc и клик по подложке закрывают**, прокрутка страницы под диалогом погашена; пока
 *   действие идёт, закрыть нельзя — иначе ошибку показать будет некуда;
 * - **форма**: Enter в поле ввода (подтверждение вводом имени) подтверждает сам; фокус при
 *   открытии — на Cancel, чтобы рефлекторный Enter ничего не удалил;
 * - **ошибка действия** — строкой над кнопками, диалог остаётся открытым.
 *
 * Не общий `Popup`: тот синглтон на Redux с закрытым перечнем типов, а подтверждению нужны
 * свои данные и колбэк. z-125 — выше переключателя темы (120), как у пикера стартового протокола.
 *
 * ⚠ Имена на иврите внутри английского заголовка оборачивай в `<bdi>` — иначе кавычки вокруг
 * имени встают задом наперёд.
 */
export default function ConfirmDialog({
  title, children, confirmLabel, busyLabel, cancelLabel = 'Cancel', confirmDisabled = false, onConfirm, onClose,
}: {
  title: React.ReactNode;
  /** Тело: что именно пропадёт, поле ввода имени и т.п. */
  children?: React.ReactNode;
  confirmLabel: string;
  /** Подпись кнопки, пока действие идёт («Deleting…»). По умолчанию — `confirmLabel`. */
  busyLabel?: string;
  cancelLabel?: string;
  /** Подтверждать пока нельзя (перечень не загрузился, имя не совпало). */
  confirmDisabled?: boolean;
  /** Само действие. Вернуло неуспех — показываем ошибку; успех — закрывает вызывающий. */
  onConfirm: () => Promise<ConfirmResult>;
  onClose: () => void;
}) {
  const deepThemeClass = useDeepThemeClass();
  const titleId = useId();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape' && !busy) onClose(); };
    window.addEventListener('keydown', onKey);
    const prev = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    return () => {
      window.removeEventListener('keydown', onKey);
      document.body.style.overflow = prev;
    };
  }, [onClose, busy]);

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (confirmDisabled || busy) return;
    setBusy(true);
    setError(null);
    const result = await onConfirm();
    if (!result.success) {
      setBusy(false);
      setError(result.error ?? 'Something went wrong');
    }
  };

  return createPortal(
    <div
      className={`fixed inset-0 z-[125] flex items-end justify-center sm:items-center ${deepThemeClass}`}
      style={{ background: 'var(--t-scrim)' }}
      onClick={() => { if (!busy) onClose(); }}
      role="dialog"
      aria-modal="true"
      aria-labelledby={titleId}
    >
      <form
        onSubmit={submit}
        className="w-full rounded-t-[18px] border border-[var(--t-border)] bg-[var(--t-surface-strong)] p-5 text-[var(--t-text)] shadow-[var(--t-shadow)] sm:w-[min(92vw,480px)] sm:rounded-[18px]"
        // На телефоне диалог прижат к низу — уводим кнопки из-под домашней полосы iOS.
        style={{ paddingBottom: 'calc(20px + env(safe-area-inset-bottom))' }}
        onClick={(e) => e.stopPropagation()}
      >
        <h2 id={titleId} className="text-[16px] font-black">{title}</h2>

        {children}

        {error && <p className="mt-3 text-[12.5px] font-bold text-[var(--t-danger)]" role="alert">{error}</p>}

        <div className="mt-5 flex justify-end gap-2">
          <button
            type="button"
            // Фокус — на отмене, не на подтверждении: действие опасное, и рефлекторный Enter
            // должен закрывать диалог, а не удалять. Enter в поле ввода подтверждает — форма.
            autoFocus
            onClick={onClose}
            disabled={busy}
            className="cursor-pointer rounded-[9px] border border-[var(--t-accent-border)] bg-transparent px-3 py-[7px] text-[12px] font-extrabold text-[var(--t-accent)] transition-colors hover:bg-[var(--t-accent-soft)] disabled:cursor-not-allowed disabled:opacity-40"
          >
            {cancelLabel}
          </button>
          <button
            type="submit"
            disabled={confirmDisabled || busy}
            className="cursor-pointer rounded-[9px] border border-[var(--t-danger-border)] bg-[var(--t-danger-soft)] px-3 py-[7px] text-[12px] font-extrabold text-[var(--t-danger)] transition-colors disabled:cursor-not-allowed disabled:opacity-40"
          >
            {busy ? busyLabel ?? confirmLabel : confirmLabel}
          </button>
        </div>
      </form>
    </div>,
    document.body,
  );
}
