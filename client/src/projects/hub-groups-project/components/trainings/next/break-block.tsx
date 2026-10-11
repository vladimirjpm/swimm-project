import React, { useState } from 'react';
import { useTrainingsTab } from '../trainings-context';
import { backOnLabel } from './next-format';

/**
 * Перерыв участника в виде Next (Т5): строка на телефоне (2a) и ссылка в ряду заметок на
 * десктопе (3a). Форма «Taking a break» — шторка из реестра (`openSheet({ kind: 'break' })`).
 *
 * ⚠ `until` перерыва — ПОСЛЕДНИЙ день; «back on» = `until + 1 день` (`backOnLabel`).
 * Статус «не тренируется с группой» (`kind === 'not_training'`, Р74) ставит только тренер —
 * кнопок нет, только пояснение.
 */

const NOT_TRAINING_TEXT =
  'The coach marked you as not training with the group — if you come, answer and you’re counted that day.';

/** Что показывать: участник + перерывы загружены. */
function useBreakView() {
  const { rsvp, breaks, openSheet } = useTrainingsTab();
  const show = !!rsvp?.rsvp?.is_member && breaks.data != null;
  const mine = breaks.data?.mine ?? null;
  const kind: 'none' | 'break' | 'not_training' = !mine ? 'none' : mine.kind === 'not_training' ? 'not_training' : 'break';
  return { show, mine, kind, breaks, openSheet };
}

function onBreakText(until: string | null | undefined): string {
  return until ? `⏸ On break · back on ${backOnLabel(until)}` : '⏸ On break';
}

/** «I'm back» — снимает перерыв; ошибку сервера показывает рядом. */
function useImBack() {
  const { breaks } = useTrainingsTab();
  const [error, setError] = useState<string | null>(null);
  const run = async () => {
    setError(null);
    const e = await breaks.setBreak({ on_break: false });
    if (e) setError(e);
  };
  return { run, error, busy: breaks.busy };
}

/** Строка перерыва телефона: пунктирная рамка, без карточки. */
export function BreakRow() {
  const { show, mine, kind, openSheet } = useBreakView();
  const back = useImBack();
  if (!show) return null;

  const shell = 'flex min-h-[48px] items-center gap-2 rounded-[12px] border px-3 text-[12.5px] font-bold';
  const shellStyle: React.CSSProperties = { borderStyle: 'dashed', borderWidth: 1, borderColor: 'var(--deep-card-border)', color: 'var(--t-text-3)' };

  if (kind === 'not_training') {
    return <div className={`${shell} py-2`} style={shellStyle}>{NOT_TRAINING_TEXT}</div>;
  }

  if (kind === 'break') {
    return (
      <div>
        <div className={shell} style={shellStyle}>
          <span className="min-w-0 flex-1">{onBreakText(mine?.until)}</span>
          <button
            type="button"
            disabled={back.busy}
            onClick={() => { void back.run(); }}
            className="cursor-pointer border-0 bg-transparent p-0 text-[12.5px] font-extrabold disabled:opacity-50"
            style={{ color: 'var(--deep-accent)' }}
          >
            I&apos;m back
          </button>
        </div>
        {back.error && <p className="m-0 mt-1 text-[12px] font-extrabold" style={{ color: 'var(--deep-danger)' }}>{back.error}</p>}
      </div>
    );
  }

  return (
    <button
      type="button"
      onClick={() => openSheet({ kind: 'break' })}
      className={`${shell} w-full cursor-pointer bg-transparent text-left`}
      style={shellStyle}
    >
      <span aria-hidden="true">⏸</span>
      <span className="flex-1">Taking a break?</span>
      <span className="font-extrabold" style={{ color: 'var(--deep-accent)' }}>Set dates ›</span>
    </button>
  );
}

/** Десктоп: ссылка в ряду заметок — «⏸ Taking a break?» либо статус перерыва с «I'm back». */
export function BreakLink() {
  const { show, mine, kind, openSheet } = useBreakView();
  const back = useImBack();
  if (!show) return null;

  if (kind === 'not_training') {
    return <span className="ml-auto text-[12px] font-bold" style={{ color: 'var(--t-text-3)' }}>{NOT_TRAINING_TEXT}</span>;
  }

  if (kind === 'break') {
    return (
      <span className="ml-auto flex flex-wrap items-center gap-x-2 text-[12px] font-bold" style={{ color: 'var(--t-text-3)' }}>
        <span>{onBreakText(mine?.until)}</span>
        <span aria-hidden="true">·</span>
        <button
          type="button"
          disabled={back.busy}
          onClick={() => { void back.run(); }}
          className="cursor-pointer border-0 bg-transparent p-0 text-[12px] font-extrabold disabled:opacity-50"
          style={{ color: 'var(--deep-accent)' }}
        >
          I&apos;m back
        </button>
        {back.error && <span className="font-extrabold" style={{ color: 'var(--deep-danger)' }}>{back.error}</span>}
      </span>
    );
  }

  return (
    <button
      type="button"
      onClick={() => openSheet({ kind: 'break' })}
      className="ml-auto cursor-pointer border-0 bg-transparent p-0 text-[12px] font-extrabold"
      style={{ color: 'var(--deep-accent)' }}
    >
      ⏸ Taking a break?
    </button>
  );
}

/** Показывается ли на десктопе что-то про перерыв (ряд заметок нужен, даже если заметок нет). */
export function useHasBreakLink(): boolean {
  return useBreakView().show;
}
