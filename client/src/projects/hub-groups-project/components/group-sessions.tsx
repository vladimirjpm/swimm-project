import React, { useState } from 'react';
import ConfirmDialog from '../../components/confirm-dialog/confirm-dialog';
import TrainingMediaStrip from '../../training-table/training-by-session/training-media-strip';
import { todayInIsrael } from './lane-plans-api';
import { sessionLabel, useTrainingSessions, type TrainingSessionsState } from '../use-training-sessions';
import type {
  TrainingSession, TrainingSessionInput, TrainingSessionMedia, TrainingSessionMediaInput,
} from '../types';

/**
 * Карточка «Trainings & videos» в табе Trainings → Sessions (Е3,
 * docs/plans/group-events-media-plan.md). Тренер заводит тренировку кнопкой — и без времён
 * («Underwater filming», решение Влада 30.09.2026: отдельного типа «событие» нет, что это
 * съёмка — видно из названия); к тренировке добавляют ссылки на видео / фото / альбом:
 * тренер — всегда, участник — если группа не поставила «только тренер». Удалить: своё — автор,
 * любое — тренер (флаг `can_delete` считает сервер).
 *
 * Данные личные (`GET /api/hub-groups/{id}/sessions`), карточка рендерится только внутри таба,
 * который виден управляющим и участникам. Тренировки с временами тут тоже есть — их можно
 * переименовать и добавить к ним видео; сами времена — в журнале тренировок ниже.
 */

/** Сколько тренировок видно сразу; остальные — под «Show all». */
const FIRST_PAGE = 6;

const inputCls =
  'rounded-[9px] border border-[var(--t-border)] bg-[var(--t-input-bg)] px-2.5 py-[6px] text-[12.5px] text-[var(--t-text)]';

const MONTH = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
const WEEKDAY = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];

/** «Wed, Sep 30» — год только если не текущий: журнал тянется на несколько сезонов. */
function formatSessionDate(date: string): string {
  const [y, m, d] = date.split('-').map(Number);
  const wd = WEEKDAY[new Date(Date.UTC(y, m - 1, d)).getUTCDay()];
  const thisYear = Number(todayInIsrael().slice(0, 4));
  return `${wd}, ${MONTH[m - 1]} ${d}${y !== thisYear ? ` ${y}` : ''}`;
}

type LinkKind = 'video' | 'image' | 'album';

/** Тип ссылки по адресу — подсказка формы, человек может поправить. */
function guessKind(url: string): LinkKind {
  const u = url.trim().toLowerCase();
  if (/photos\.app\.goo\.gl|photos\.google\.com|drive\.google\.com\/drive\/folders|chat\.whatsapp\.com/.test(u)) return 'album';
  if (/\.(jpe?g|png|webp|gif|heic)(\?|#|$)/.test(u)) return 'image';
  return 'video';
}

/** Источник для сервера: у видео — хостинг по адресу, у фото — other, у альбома — album. */
function sourceOf(kind: LinkKind, url: string): TrainingSessionMediaInput['source_type'] {
  if (kind === 'album') return 'album';
  if (kind === 'image') return 'other';
  const u = url.toLowerCase();
  if (/youtube\.com|youtu\.be/.test(u)) return 'youtube';
  if (/vimeo\.com/.test(u)) return 'vimeo';
  return 'other';
}

function SmallButton({
  children, onClick, disabled, tone = 'plain', title,
}: {
  children: React.ReactNode;
  onClick: () => void;
  disabled?: boolean;
  tone?: 'plain' | 'coach' | 'danger' | 'accent';
  title?: string;
}) {
  const style: React.CSSProperties = tone === 'coach'
    ? { borderColor: 'var(--deep-ow-border)', background: 'var(--deep-ow-chip)', color: 'var(--deep-ow)' }
    : tone === 'accent'
      ? { borderColor: 'var(--deep-accent-border)', background: 'var(--deep-accent-chip)', color: 'var(--deep-accent)' }
      : tone === 'danger'
        ? { borderColor: 'color-mix(in srgb, var(--deep-danger) 30%, transparent)', background: 'transparent', color: 'var(--deep-danger)' }
        : { borderColor: 'var(--deep-card-border)', background: 'var(--deep-card-bg)', color: 'var(--deep-text-mute)' };
  return (
    <button
      type="button"
      onClick={onClick}
      disabled={disabled}
      title={title}
      className="h-[30px] cursor-pointer rounded-[9px] border px-2.5 text-[12px] font-extrabold disabled:cursor-default disabled:opacity-50"
      style={style}
    >
      {children}
    </button>
  );
}

function ErrorLine({ error }: { error: string | null }) {
  if (!error) return null;
  return <p className="m-0 text-[12px] font-extrabold" style={{ color: 'var(--deep-danger)' }} role="alert">{error}</p>;
}

/** Форма тренировки: новая (дата — сегодня) или правка. У тренировки с временами дата закрыта. */
function SessionForm({
  initial, dateLocked, busy, onSave, onCancel,
}: {
  initial?: TrainingSession;
  dateLocked?: boolean;
  busy: boolean;
  onSave: (input: TrainingSessionInput) => Promise<string | null>;
  onCancel: () => void;
}) {
  const [date, setDate] = useState(initial?.date ?? todayInIsrael());
  // У залитой скриптом названия нет — предлагаем её план (сервер держит до 100 символов).
  const [title, setTitle] = useState(initial?.title ?? initial?.plan?.slice(0, 100) ?? '');
  const [start, setStart] = useState(initial?.start ?? '');
  const [note, setNote] = useState(initial?.note ?? '');
  const [error, setError] = useState<string | null>(null);

  const save = async () => {
    setError(null);
    const e = await onSave({ date, title, start: start || null, note: note || null });
    if (e) setError(e);
  };

  return (
    <div
      className="flex flex-col gap-2 rounded-[12px] border p-3"
      style={{ borderColor: 'var(--deep-ow-border)', background: 'var(--deep-card-bg-row)' }}
    >
      <div className="flex flex-wrap gap-2">
        <label className="flex flex-col gap-1 text-[11px] font-bold" style={{ color: 'var(--deep-text-mute)' }}>
          Date
          <input
            type="date"
            value={date}
            disabled={dateLocked}
            title={dateLocked ? 'This training has recorded times — its date can’t change' : undefined}
            onChange={(e) => setDate(e.target.value)}
            className={inputCls}
          />
        </label>
        <label className="flex flex-col gap-1 text-[11px] font-bold" style={{ color: 'var(--deep-text-mute)' }}>
          Start (optional)
          <input type="time" value={start} onChange={(e) => setStart(e.target.value)} className={inputCls} />
        </label>
      </div>
      <input
        value={title}
        onChange={(e) => setTitle(e.target.value)}
        placeholder="Name — e.g. “Underwater filming”"
        maxLength={100}
        dir="auto"
        className={inputCls}
      />
      <input
        value={note}
        onChange={(e) => setNote(e.target.value)}
        placeholder="Note (optional)"
        maxLength={500}
        dir="auto"
        className={inputCls}
      />
      {!initial && (
        <p className="m-0 text-[11px]" style={{ color: 'var(--deep-text-mute)' }}>
          No times needed — add the training, then attach videos to it. Empty start = the schedule’s slot that day.
        </p>
      )}
      <ErrorLine error={error} />
      <div className="flex gap-2">
        <SmallButton tone="coach" onClick={() => { void save(); }} disabled={busy || !title.trim() || !date}>
          {initial ? 'Save' : 'Add training'}
        </SmallButton>
        <SmallButton onClick={onCancel}>Cancel</SmallButton>
      </div>
    </div>
  );
}

/** «+ Add video»: ссылка, подпись, тип (угадывается по адресу). Сайт ничего не хранит — только ссылку. */
function AddLinkForm({
  busy, onAdd, onCancel,
}: {
  busy: boolean;
  onAdd: (input: TrainingSessionMediaInput) => Promise<string | null>;
  onCancel: () => void;
}) {
  const [url, setUrl] = useState('');
  const [caption, setCaption] = useState('');
  const [kind, setKind] = useState<LinkKind>('video');
  const [kindTouched, setKindTouched] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const onUrl = (next: string) => {
    setUrl(next);
    if (!kindTouched) setKind(guessKind(next));
  };

  const add = async () => {
    setError(null);
    const trimmed = url.trim();
    const e = await onAdd({ media_type: kind, source_type: sourceOf(kind, trimmed), url: trimmed, caption: caption.trim() || null });
    if (e) setError(e);
  };

  return (
    <div
      className="mt-2 flex flex-col gap-2 rounded-[12px] border p-3"
      style={{ borderColor: 'var(--deep-accent-border)', background: 'var(--deep-card-bg-row)' }}
    >
      <input
        value={url}
        onChange={(e) => onUrl(e.target.value)}
        placeholder="Link (https://…) — YouTube, Vimeo, Google Photos…"
        className={inputCls}
        inputMode="url"
      />
      <div className="flex flex-wrap gap-2">
        <select
          value={kind}
          onChange={(e) => { setKind(e.target.value as LinkKind); setKindTouched(true); }}
          className={`${inputCls} cursor-pointer`}
          aria-label="Link type"
        >
          <option value="video">Video</option>
          <option value="image">Photo</option>
          <option value="album">Album</option>
        </select>
        <input
          value={caption}
          onChange={(e) => setCaption(e.target.value)}
          placeholder="Caption (optional)"
          maxLength={200}
          dir="auto"
          className={`${inputCls} min-w-0 flex-1`}
        />
      </div>
      <p className="m-0 text-[11px]" style={{ color: 'var(--deep-text-mute)' }}>
        Only the group sees it. The site keeps just the link.
      </p>
      <ErrorLine error={error} />
      <div className="flex gap-2">
        <SmallButton tone="accent" onClick={() => { void add(); }} disabled={busy || !url.trim()}>Add</SmallButton>
        <SmallButton onClick={onCancel}>Cancel</SmallButton>
      </div>
    </div>
  );
}

function SessionRow({
  session, state, canManage, canAddMedia,
}: {
  session: TrainingSession;
  state: TrainingSessionsState;
  canManage: boolean;
  canAddMedia: boolean;
}) {
  const [mode, setMode] = useState<'view' | 'edit' | 'add'>('view');
  // Что ждёт подтверждения: вся тренировка или одна ссылка (общий ConfirmDialog, ui-components.md).
  const [pending, setPending] = useState<{ kind: 'session' } | { kind: 'link'; media: TrainingSessionMedia } | null>(null);

  const confirmDialog = pending && (
    <ConfirmDialog
      // Название — в <bdi>: иврит внутри английской фразы иначе разворачивает кавычки.
      title={pending.kind === 'session'
        ? <>Delete “<bdi>{sessionLabel(session)}</bdi>”?</>
        : pending.media.mine ? 'Remove your link?' : 'Remove this link for everyone?'}
      confirmLabel={pending.kind === 'session' ? 'Delete training' : 'Remove'}
      busyLabel={pending.kind === 'session' ? 'Deleting…' : 'Removing…'}
      onConfirm={async () => {
        const error = pending.kind === 'session'
          ? await state.remove(session.id)
          : await state.removeMedia(session.id, pending.media.id);
        if (!error) setPending(null);
        return { success: !error, error: error ?? undefined };
      }}
      onClose={() => setPending(null)}
    >
      <p className="mt-3 text-[13px] text-[var(--t-text-2)]">
        {pending.kind === 'session'
          ? (session.media.length > 0
            ? `Its ${session.media.length} link${session.media.length === 1 ? '' : 's'} go too.`
            : 'It has no links.')
          : pending.media.mine
            ? 'The group will no longer see it.'
            : 'Someone else added it — the group will no longer see it.'}
      </p>
    </ConfirmDialog>
  );

  if (mode === 'edit') {
    return (
      <SessionForm
        initial={session}
        dateLocked={session.has_results}
        busy={state.busy}
        onCancel={() => setMode('view')}
        onSave={async (input) => {
          const e = await state.update(session.id, { ...input, pool_type: session.pool_type ?? null });
          if (!e) setMode('view');
          return e;
        }}
      />
    );
  }

  return (
    <div
      className="flex flex-col gap-2 px-3 py-2.5"
      style={{ background: 'var(--deep-card-bg-row)', borderRadius: 'var(--deep-radius-row)' }}
    >
      <div className="flex flex-wrap items-baseline justify-between gap-x-3 gap-y-1">
        <div dir="auto" className="min-w-0 text-left text-[13.5px] font-extrabold" style={{ color: 'var(--deep-text)' }}>
          {sessionLabel(session)}
        </div>
        <div className="hp-mono text-[11.5px] font-extrabold" style={{ color: 'var(--deep-text-mute)' }}>
          {formatSessionDate(session.date)}{session.start ? ` · ${session.start}` : ''}
          {session.has_results && <span title="Recorded times — in the training log"> · ⏱ times</span>}
        </div>
      </div>
      {session.note && (
        <div dir="auto" className="text-left text-[12px]" style={{ color: 'var(--deep-text-mute)' }}>{session.note}</div>
      )}

      <TrainingMediaStrip
        media={session.media}
        className="tbs-media-strip--flush"
        onRemove={(m) => setPending({ kind: 'link', media: m })}
        canRemove={(m) => m.can_delete}
      />

      {mode === 'add' && (
        <AddLinkForm
          busy={state.busy}
          onCancel={() => setMode('view')}
          onAdd={async (input) => {
            const e = await state.addMedia(session.id, input);
            if (!e) setMode('view');
            return e;
          }}
        />
      )}

      {mode === 'view' && (canAddMedia || canManage) && (
        <div className="flex flex-wrap gap-2">
          {canAddMedia && <SmallButton tone="accent" onClick={() => setMode('add')}>+ Add video</SmallButton>}
          {canManage && <SmallButton tone="coach" onClick={() => setMode('edit')}>Edit</SmallButton>}
          {canManage && !session.has_results && (
            <SmallButton tone="danger" onClick={() => setPending({ kind: 'session' })} disabled={state.busy}>Delete</SmallButton>
          )}
        </div>
      )}
      {confirmDialog}
    </div>
  );
}

function GroupSessions({ groupId }: { groupId: number }) {
  const state = useTrainingSessions(groupId, true);
  const [creating, setCreating] = useState(false);
  const [showAll, setShowAll] = useState(false);
  const data = state.data;

  if (!data) {
    return state.loadError
      ? <section className="deep-card mb-4"><ErrorLine error={state.loadError} /></section>
      : null;
  }

  const sessions = showAll ? data.sessions : data.sessions.slice(0, FIRST_PAGE);
  return (
    <section className="deep-card mb-4">
      <div className="flex flex-wrap items-start justify-between gap-2">
        <div>
          <div className="deep-card-title">Trainings &amp; videos</div>
          <div className="deep-card-sub mt-1">
            private — group members and admins
            {!data.can_add_media && ' · only the coach adds videos'}
          </div>
        </div>
        {data.can_manage && !creating && (
          <SmallButton tone="coach" onClick={() => setCreating(true)}>+ New training</SmallButton>
        )}
      </div>

      <div className="mt-3 flex flex-col gap-2">
        {creating && (
          <SessionForm
            busy={state.busy}
            onCancel={() => setCreating(false)}
            onSave={async (input) => {
              const e = await state.create(input);
              if (!e) setCreating(false);
              return e;
            }}
          />
        )}

        {data.sessions.length === 0 && !creating && (
          <p className="m-0 text-[12.5px] font-bold" style={{ color: 'var(--deep-text-mute)' }}>
            {data.can_manage
              ? 'No trainings yet. Add one — e.g. “Underwater filming” — and attach videos to it. Times aren’t needed.'
              : 'No trainings yet.'}
          </p>
        )}

        {sessions.map((s) => (
          <SessionRow
            key={s.id}
            session={s}
            state={state}
            canManage={data.can_manage}
            canAddMedia={data.can_add_media}
          />
        ))}

        {data.sessions.length > FIRST_PAGE && (
          <div>
            <SmallButton onClick={() => setShowAll((v) => !v)}>
              {showAll ? 'Show fewer' : `Show all ${data.sessions.length}`}
            </SmallButton>
          </div>
        )}
      </div>
    </section>
  );
}

export default GroupSessions;
