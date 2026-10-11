import React from 'react';
import { MARK_COLOR, type TrainingMark } from './use-training-marks';

/**
 * Рамка секции таба Trainings — ОДНА на все секции (docs/plans/trainings-tab-redesign-plan.md
 * §3½): карточка, заголовок с подписью, место под метку внимания и кнопки, состояния
 * loading / error / empty. Внешний вид рамки меняется здесь, а не в каждой секции.
 *
 * Секция отдаёт сюда только смысл (`status`, тексты); готовое содержимое — `children`, оно
 * рисуется при `status = 'ready'`. `bare` — без карточки: блок сам несёт рамку (вид Lanes:
 * шапка с датой, доска или редактор дорожек).
 */

export type TrainingsSectionStatus = 'ready' | 'loading' | 'error' | 'empty';

export function MarkDot({ mark }: { mark: TrainingMark }) {
  return (
    <span
      className="inline-flex shrink-0 items-center justify-center rounded-full text-[10px] font-black leading-none"
      style={mark.count != null
        ? { minWidth: 18, height: 18, padding: '0 5px', background: MARK_COLOR[mark.tone], color: 'var(--deep-accent-ink)' }
        : { width: 7, height: 7, background: MARK_COLOR[mark.tone] }}
      role="img"
      aria-label={mark.label}
      title={mark.label}
    >
      {mark.count != null ? mark.count : null}
    </span>
  );
}

export function TrainingsSection({
  id, title, sub, mark = null, actions, status = 'ready', error, empty, bare = false, style, children,
}: {
  /** Имя секции из реестра — `data-section` в DOM: увидел в инспекторе → нашёл файл. */
  id: string;
  title?: React.ReactNode;
  sub?: React.ReactNode;
  mark?: TrainingMark | null;
  /** Кнопки справа от заголовка (`+ New training`, `✎ Edit lanes`). */
  actions?: React.ReactNode;
  status?: TrainingsSectionStatus;
  /** Текст при `status = 'error'`. */
  error?: string | null;
  /** Содержимое при `status = 'empty'`. */
  empty?: React.ReactNode;
  bare?: boolean;
  /** Точечный оверрайд рамки карточки (рамка `--deep-live-border`, свой padding) — не для цветов по месту. */
  style?: React.CSSProperties;
  children?: React.ReactNode;
}) {
  const body =
    status === 'loading' ? (
      <p className="m-0 text-[12.5px] font-bold" style={{ color: 'var(--deep-text-mute)' }}>Loading…</p>
    ) : status === 'error' ? (
      <p className="m-0 text-[12.5px] font-extrabold" style={{ color: 'var(--deep-danger)' }} role="alert">
        {error || 'Could not load. Try again.'}
      </p>
    ) : status === 'empty' ? (
      <div className="text-[12.5px] font-bold" style={{ color: 'var(--deep-text-mute)' }}>{empty}</div>
    ) : children;

  if (bare) return <div data-section={id} className="min-w-0">{body}</div>;

  const hasHeader = title != null || actions != null;
  return (
    <section data-section={id} className="deep-card mb-0 min-w-0" style={style}>
      {hasHeader && (
        <div className="flex flex-wrap items-start justify-between gap-x-4 gap-y-2">
          <div className="min-w-0">
            {title != null && (
              <div className="deep-card-title flex items-center gap-2">
                <span className="min-w-0">{title}</span>
                {mark && <MarkDot mark={mark} />}
              </div>
            )}
            {sub != null && <div className="deep-card-sub mt-1">{sub}</div>}
          </div>
          {actions != null && <div className="flex shrink-0 flex-wrap items-center gap-2">{actions}</div>}
        </div>
      )}
      <div className={hasHeader ? 'mt-3' : ''}>{body}</div>
    </section>
  );
}
