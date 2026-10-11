import React, { useState } from 'react';
import ConfirmDialog from '../../../../components/confirm-dialog/confirm-dialog';
import UI_SwimTime from '../../../../components/mix/swim-time/swim-time';
import TrainingMediaStrip from '../../../../training-table/training-by-session/training-media-strip';
import { sessionLabel } from '../../../use-training-sessions';
import type { TrainingSession, TrainingSessionMedia, TrainingSessionSets } from '../../../types';
import { useTrainingsTab } from '../trainings-context';
import {
  dayOfMonth, formatKm, monthLabel, shortStyle, trainingMeta, weekdayShort,
} from './feed-format';
import SetsAndTimes from './sets-and-times';

/**
 * Одна запись ленты тренировок (прототип 2a / 3a / 3b): колонка даты + карточка с показателями,
 * строкой «You», полосой медиа и раскрываемыми на месте «Sets & times». Сама ничего не грузит:
 * сеты и «раскрыта ли» приходят от ленты, формы открываются через `openSheet` контекста.
 *
 * Над записью — разделитель месяца, когда он сменился (`monthDivider`): дата в карточке без
 * месяца, а лента тянется на несколько сезонов.
 */

const linkBtn = 'cursor-pointer border-0 bg-transparent p-0 text-[12px] font-extrabold';

function Metric({ value, label, isDesktop }: { value: React.ReactNode; label: string; isDesktop: boolean }) {
  return (
    <div>
      <div
        className="leading-none"
        style={{ fontFamily: 'var(--deep-font-display)', fontSize: isDesktop ? 17 : 16, color: 'var(--t-text)' }}
      >
        {value}
      </div>
      <div className="mt-0.5 text-[9px] font-extrabold uppercase tracking-[.05em]" style={{ color: 'var(--t-text-3)' }}>
        {label}
      </div>
    </div>
  );
}

function TrainingCard({
  session, canAddMedia, monthDivider, expanded, onToggleSets, sets, setsLoading, onToggleKey,
}: {
  session: TrainingSession;
  canAddMedia: boolean;
  /** Показать «SEPTEMBER 2026» над записью. */
  monthDivider: boolean;
  expanded: boolean;
  onToggleSets: () => void;
  sets: TrainingSessionSets | null;
  setsLoading: boolean;
  onToggleKey: (setNo: number | null) => Promise<string | null>;
}) {
  const { manages, isDesktop, openSheet, sessions } = useTrainingsTab();
  // Ссылка, ожидающая подтверждения удаления (общий ConfirmDialog, ui-components.md).
  const [pending, setPending] = useState<TrainingSessionMedia | null>(null);

  const dateCol = isDesktop ? 44 : 40;
  const gap = isDesktop ? 10 : 8;
  const newCount = session.media.filter((m) => m.is_new).length;
  // Нет ни одного видео и можно добавить — вместо ссылки «+ Add video» плитка на месте полосы.
  const noMediaTile = canAddMedia && session.media.length === 0;
  const empty = !session.has_results && session.media.length === 0;
  const stats = session.stats;
  const myKey = session.my_key;

  const youPill = myKey && (
    <div
      className={`flex items-center justify-between gap-3 rounded-[10px] text-[11.5px] font-extrabold ${isDesktop ? 'ml-auto' : 'w-full'}`}
      style={{ background: 'var(--deep-accent-chip)', padding: '7px 10px' }}
    >
      <span style={{ color: 'var(--t-text-3)' }}>
        {myKey.kind === 'me' ? 'You' : <bdi>{myKey.name || myKey.name_en}</bdi>}
      </span>
      <span className="hp-mono" style={{ color: 'var(--deep-accent)' }}>
        {myKey.distance} {shortStyle(myKey.style)} · <UI_SwimTime time={myKey.time} />
      </span>
    </div>
  );

  return (
    <div>
      {monthDivider && (
        <div
          className="pb-1.5 text-[10px] font-extrabold uppercase tracking-[.06em]"
          style={{ color: 'var(--t-text-3)', paddingLeft: dateCol + gap }}
        >
          {monthLabel(session.date)}
        </div>
      )}
      <div className="grid" style={{ gridTemplateColumns: `${dateCol}px minmax(0,1fr)`, gap }}>
        <div className="flex flex-col items-center">
          <span className="text-[9.5px] font-extrabold uppercase" style={{ color: 'var(--t-text-3)' }}>
            {weekdayShort(session.date)}
          </span>
          <span className="hp-mono font-extrabold" style={{ fontSize: isDesktop ? 16 : 15, color: 'var(--t-text)' }}>
            {dayOfMonth(session.date)}
          </span>
          <span className="mt-1 w-px flex-1" style={{ background: 'var(--deep-card-border)' }} />
        </div>

        <div
          className="mb-2.5 flex min-w-0 flex-col gap-2 rounded-[14px] border"
          style={{
            borderColor: 'var(--deep-card-border)',
            background: isDesktop ? 'var(--deep-card-bg-raised)' : 'var(--deep-card-bg)',
            padding: isDesktop ? '12px 14px' : '10px 12px',
            // С плиткой «+ Add video» не приглушаем: тренировка ждёт видео, а не «пустая» (хендофф 4a).
            opacity: empty && !noMediaTile ? 0.6 : 1,
          }}
        >
          <div className="flex items-start justify-between gap-3">
            <div className="min-w-0">
              <div
                dir="auto"
                className="truncate text-left font-extrabold"
                style={{ fontSize: isDesktop ? 14 : 13.5, color: 'var(--t-text)' }}
              >
                <bdi>{sessionLabel(session)}</bdi>
              </div>
              <div
                className="hp-mono font-bold"
                style={{ fontSize: isDesktop ? 11 : 10.5, color: 'var(--t-text-3)' }}
              >
                {trainingMeta(session, manages)}
              </div>
            </div>
            <div className="flex shrink-0 items-center gap-3">
              {newCount > 0 && (
                <span
                  className="inline-flex items-center gap-1 text-[10px] font-extrabold tracking-[.05em]"
                  style={{ color: 'var(--deep-accent)' }}
                >
                  <span className="inline-block h-[7px] w-[7px] rounded-full" style={{ background: 'var(--deep-accent)' }} />
                  {newCount} NEW
                </span>
              )}
              {manages && (
                <button
                  type="button"
                  className={linkBtn}
                  style={{ color: 'var(--deep-ow)' }}
                  onClick={() => openSheet({ kind: 'edit-training', session })}
                >
                  Edit
                </button>
              )}
              {manages && !session.has_results && (
                <button
                  type="button"
                  className={`${linkBtn} disabled:opacity-50`}
                  style={{ color: 'var(--deep-danger)' }}
                  disabled={sessions.busy}
                  onClick={() => openSheet({ kind: 'delete-training', session })}
                >
                  Delete
                </button>
              )}
            </div>
          </div>

          {(stats || myKey) && (
            <div className="flex flex-wrap items-end" style={{ columnGap: isDesktop ? 20 : 16, rowGap: 8 }}>
              {stats && (
                <>
                  <Metric value={formatKm(stats.meters)} label="km" isDesktop={isDesktop} />
                  <Metric value={stats.swimmers} label="swimmers" isDesktop={isDesktop} />
                </>
              )}
              {youPill}
            </div>
          )}

          {noMediaTile ? (
            // Нет видео, а добавить можно (хендофф 4a): плитка на месте полосы медиа.
            <div className="flex items-center gap-2.5">
              <button
                type="button"
                aria-label="Add video"
                onClick={() => openSheet({ kind: 'add-video', sessionId: session.id })}
                className="flex shrink-0 cursor-pointer flex-col items-center justify-center border bg-transparent p-0"
                style={{ width: 86, height: 60, borderRadius: 10, borderStyle: 'dashed', borderColor: 'var(--deep-card-border)', color: 'var(--deep-accent)' }}
              >
                <span aria-hidden="true" className="text-[16px] font-extrabold leading-none">+</span>
                <span className="text-[10.5px] font-extrabold leading-tight">Add video</span>
              </button>
              <span className="text-[11px] font-bold" style={{ color: 'var(--t-text-3)' }}>Only the group sees them.</span>
            </div>
          ) : (
            <TrainingMediaStrip
              media={session.media}
              className="tbs-media-strip--flush"
              size={isDesktop ? 'lg' : 'md'}
              onRemove={(m) => setPending(m)}
              canRemove={(m) => m.can_delete}
              // «Who's in it»: имя отмеченного пловца вместо подписи (иврит первым), мой — акцентом.
              tagOf={(m) => (m.swimmer ? { name: m.swimmer.name || m.swimmer.name_en, mine: !!m.for_me } : null)}
              // Лайтбокс — общий реестр форм таба (`lightbox`); индекс — в списке без альбомов.
              onOpen={(index) => openSheet({ kind: 'lightbox', sessionId: session.id, index })}
            />
          )}

          {((canAddMedia && !noMediaTile) || session.has_results) && (
            <div className="flex flex-wrap gap-4">
              {canAddMedia && !noMediaTile && (
                <button
                  type="button"
                  className={linkBtn}
                  style={{ color: 'var(--deep-accent)' }}
                  onClick={() => openSheet({ kind: 'add-video', sessionId: session.id })}
                >
                  + Add video
                </button>
              )}
              {session.has_results && (
                <button
                  type="button"
                  className={linkBtn}
                  style={{ color: 'var(--deep-accent)' }}
                  aria-expanded={expanded}
                  onClick={onToggleSets}
                >
                  Sets &amp; times {expanded ? '▴' : '›'}
                </button>
              )}
            </div>
          )}

          {session.has_results && expanded && (
            <SetsAndTimes
              data={sets}
              loading={setsLoading}
              canManage={manages}
              busy={sessions.busy}
              onToggleKey={onToggleKey}
            />
          )}
        </div>
      </div>

      {pending && (
        <ConfirmDialog
          title={pending.mine ? 'Remove your link?' : 'Remove this link for everyone?'}
          confirmLabel="Remove"
          busyLabel="Removing…"
          onConfirm={async () => {
            const error = await sessions.removeMedia(session.id, pending.id);
            if (!error) setPending(null);
            return { success: !error, error: error ?? undefined };
          }}
          onClose={() => setPending(null)}
        >
          <p className="mt-3 text-[13px] text-[var(--t-text-2)]">
            {pending.mine ? 'The group will no longer see it.' : 'Someone else added it — the group will no longer see it.'}
          </p>
        </ConfirmDialog>
      )}
    </div>
  );
}

export default TrainingCard;
