import React from 'react';

/**
 * Экран S3 — «стартовый протокол ещё не опубликован» (шаг Т9, хендофф §1.4).
 *
 * Раньше на этом месте стояла одна серая строка «No start list published yet», и она
 * читалась как поломка: человек пришёл по ссылке, а ему нечего показать и непонятно,
 * ждать ли. Экран отвечает на три вопроса сразу: это нормально, когда ждать и что сделать.
 *
 * Скелет-полосы наверху — не украшение: они показывают, ЧТО тут появится, когда посев
 * сделают, и отличают «ещё нет» от «ничего и не будет».
 */
export default function NotPublished({
  startsLabel, isAuthenticated, notifyMe, lastChecked, onCheckAgain, onToggleNotify,
}: {
  /** «Sun 15 Feb» — когда старт. null, если дату мы отсюда не знаем. */
  startsLabel: string | null;
  isAuthenticated: boolean;
  notifyMe: boolean;
  /** «HH:MM» последней попытки — иначе кнопка «Check again» выглядит бесследной. */
  lastChecked: string | null;
  onCheckAgain: () => void;
  /** null — кнопки «Notify me» нет (гостю её показывать не за что: подписка на аккаунте). */
  onToggleNotify: ((next: boolean) => void) | null;
}) {
  return (
    <div className="py-2" style={{ color: 'var(--deep-text)' }}>
      {/* Скелет: три строки-заглушки в форме будущих строк заплывов. */}
      <div aria-hidden className="mb-5 space-y-2">
        {[0, 1, 2].map((i) => (
          <div
            key={i}
            className="flex items-center gap-2.5 rounded-[12px] border p-2.5"
            style={{ borderColor: 'var(--deep-card-border)', background: 'var(--deep-card-bg)', opacity: 1 - i * 0.25 }}
          >
            <span className="h-9 w-[74px] shrink-0 rounded-[10px]" style={{ background: 'var(--deep-divider)' }} />
            <span className="flex-1 space-y-1.5">
              <span className="block h-3 w-2/5 rounded" style={{ background: 'var(--deep-divider)' }} />
              <span className="block h-3 w-3/5 rounded" style={{ background: 'var(--deep-divider)' }} />
            </span>
          </div>
        ))}
      </div>

      <h3 className="text-[17px] font-black">Start list not published yet</h3>
      <p className="mt-1 text-[13px]" style={{ color: 'var(--deep-text-mute)' }}>
        Protocols usually appear a few days before the meet.
        {startsLabel ? ` Competition starts ${startsLabel}.` : ''}
      </p>

      <div className="mt-4 flex flex-wrap items-center gap-2">
        <button
          type="button"
          onClick={onCheckAgain}
          className="rounded-[12px] border px-3 py-2 text-[13px] font-black"
          style={{ borderColor: 'var(--deep-card-border)', background: 'var(--deep-card-bg)' }}
        >
          ↻ Check again
        </button>

        {/* «Notify me» — ТОЛЬКО залогиненному: подписка живёт на аккаунте, гостю её негде
            хранить. ⚠ Рассылки за кнопкой пока НЕТ (решение Влада 29.08.2026: делаем
            кнопку, механизм уведомлений — отдельная работа); флаг копится в плане, чтобы,
            когда механизм появится, было кого уведомить. */}
        {isAuthenticated && onToggleNotify && (
          <button
            type="button"
            onClick={() => onToggleNotify(!notifyMe)}
            aria-pressed={notifyMe}
            className="rounded-[12px] border px-3 py-2 text-[13px] font-black"
            style={{
              background: notifyMe ? 'var(--theme-personal-badge-bg)' : 'var(--theme-personal-bg)',
              borderColor: 'var(--theme-personal-border)',
              color: 'var(--theme-personal-accent)',
            }}
          >
            {notifyMe ? '⭐ You’ll be notified' : '⭐ Notify me when it’s out'}
          </button>
        )}
      </div>

      {lastChecked && (
        <p className="mt-3 text-[11px]" style={{ color: 'var(--deep-text-faint)' }}>
          Last checked {lastChecked}
        </p>
      )}
    </div>
  );
}
