import { useState } from 'react';
import { useHubGroupMedia } from '../use-my-hub-groups';
import type { HubGroupMediaInput } from '../my-groups-types';
import type { HubGroupMediaItem } from '../../../utils/interfaces/results';
import { inputCls, btnCls, btnDangerCls } from './my-groups-shared';

const EMPTY_MEDIA_INPUT: HubGroupMediaInput = {
  mediaType: 'image', sourceType: 'other', url: '', caption: '', trainingId: null,
};

/** Строка списка медиа (галерея или тренировка) с кнопкой удаления. */
function MediaListRow({ item, onRemove }: { item: HubGroupMediaItem; onRemove: () => void }) {
  return (
    <li className="flex items-center justify-between gap-2">
      <span className="min-w-0 truncate text-[13px] text-[var(--t-text)]">
        {item.media_type === 'album' ? '📂' : item.media_type === 'video' ? '🎬' : '🖼️'} {item.caption || item.url}
      </span>
      <button type="button" className={btnDangerCls} onClick={onRemove}>✕</button>
    </li>
  );
}

/** Редактор медиа: публичная галерея группы + медиа тренировок (владелец/админ). */
export function MediaEditor({ hubGroupId, slug }: { hubGroupId: number; slug: string }) {
  const media = useHubGroupMedia(hubGroupId, slug);
  const [form, setForm] = useState<HubGroupMediaInput>(EMPTY_MEDIA_INPUT);
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const [swimmerResults, setSwimmerResults] = useState<Awaited<ReturnType<typeof media.getSwimmerResults>>>([]);

  const setField = <K extends keyof HubGroupMediaInput>(key: K, value: HubGroupMediaInput[K]) =>
    setForm((f) => ({ ...f, [key]: value }));

  const onSwimmerAnchorChange = async (swimmerId: number | null) => {
    setForm((f) => ({ ...f, swimmerId, resultId: null }));
    setSwimmerResults(swimmerId != null ? await media.getSwimmerResults(swimmerId) : []);
  };

  const onKindChange = (kind: 'image' | 'video' | 'album') => {
    if (kind === 'album') setForm((f) => ({ ...f, mediaType: 'album', sourceType: 'album' }));
    else setForm((f) => ({ ...f, mediaType: kind, sourceType: kind === 'video' ? 'youtube' : 'other' }));
  };

  const kind: 'image' | 'video' | 'album' =
    form.mediaType === 'album' ? 'album' : form.mediaType === 'video' ? 'video' : 'image';

  const submit = async () => {
    setSaving(true);
    setError(null);
    const result = await media.addMedia(form);
    setSaving(false);
    if (result.success) { setForm(EMPTY_MEDIA_INPUT); setSwimmerResults([]); }
    else setError(result.error ?? 'Save failed');
  };

  return (
    <div className="flex flex-col gap-2">
      <p className="text-[11.5px] italic text-[var(--t-text-3)]">
        No training set = public gallery (visible on the group page). Pick a training to attach media to it instead.
      </p>

      {media.loadError && (
        <p className="text-[12.5px] font-bold text-[var(--t-danger)]">{media.loadError}</p>
      )}

      <ul className="m-0 flex list-none flex-col gap-2 p-0">
        {media.gallery.map((m) => (
          <MediaListRow key={m.id} item={m} onRemove={() => media.removeMedia(m.id)} />
        ))}
        {media.gallery.length === 0 && !media.loadError && (
          <p className="text-[12.5px] text-[var(--t-text-2)]">No public gallery items yet.</p>
        )}
      </ul>

      {/* Медиа, привязанное к тренировкам, — иначе после добавления его негде увидеть/удалить. */}
      {media.trainings.filter((t) => t.media.length > 0).map((t) => (
        <div key={t.sessionId} className="flex flex-col gap-1">
          <p className="m-0 text-[11.5px] font-extrabold uppercase tracking-[0.12em] text-[var(--t-accent-dim)]">
            🔒 {t.label}
          </p>
          <ul className="m-0 flex list-none flex-col gap-2 p-0">
            {t.media.map((m) => (
              <MediaListRow key={m.id} item={m} onRemove={() => media.removeMedia(m.id)} />
            ))}
          </ul>
        </div>
      ))}

      <div className="flex flex-wrap gap-2">
        <select className={`${inputCls} max-w-[110px]`} value={kind}
          onChange={(e) => onKindChange(e.target.value as 'image' | 'video' | 'album')}>
          <option value="image">Image</option>
          <option value="video">Video</option>
          <option value="album">Album</option>
        </select>
        {kind === 'video' && (
          <select className={`${inputCls} max-w-[110px]`} value={form.sourceType}
            onChange={(e) => setField('sourceType', e.target.value)}>
            <option value="youtube">YouTube</option>
            <option value="vimeo">Vimeo</option>
            <option value="other">Other</option>
          </select>
        )}
        <input className={inputCls} placeholder="URL (https://…)" value={form.url}
          onChange={(e) => setField('url', e.target.value)} />
        <input className={inputCls} placeholder="Caption (optional)" value={form.caption ?? ''}
          onChange={(e) => setField('caption', e.target.value)} />
        {/* Куда: витрина / members-разборы (2B′, только официальная группа) / тренировка. */}
        <select className={`${inputCls} max-w-[220px]`}
          value={form.visibility === 'members' ? 'members' : (form.trainingId ?? '')}
          onChange={(e) => {
            const v = e.target.value;
            if (v === 'members') setForm((f) => ({ ...f, visibility: 'members', trainingId: null }));
            else setForm((f) => ({
              ...f, visibility: 'public', swimmerId: null,
              trainingId: v ? Number(v) : null,
            }));
          }}>
          <option value="">Public gallery</option>
          {media.isOfficial && <option value="members">🔒 Members reviews</option>}
          {media.trainings.map((t) => (
            <option key={t.sessionId} value={t.sessionId}>{t.label}</option>
          ))}
        </select>
        {form.visibility === 'members' && (
          <select className={`${inputCls} max-w-[220px]`} value={form.swimmerId ?? ''}
            onChange={(e) => onSwimmerAnchorChange(e.target.value ? Number(e.target.value) : null)}>
            <option value="">— group media —</option>
            {media.groupSwimmers.map((s) => (
              <option key={s.id} value={s.id}>{s.name}</option>
            ))}
          </select>
        )}
        {form.visibility === 'members' && form.swimmerId != null && swimmerResults.length > 0 && (
          <select className={`${inputCls} max-w-[260px]`} value={form.resultId ?? ''}
            onChange={(e) => setField('resultId', e.target.value ? Number(e.target.value) : null)}>
            <option value="">— not tied to a swim —</option>
            {swimmerResults.map((r) => (
              <option key={r.id} value={r.id}>
                {r.date} · {r.styleName} {r.distance} · {r.time} ({r.competition})
              </option>
            ))}
          </select>
        )}
        <button type="button" className={btnCls} disabled={saving || !form.url.trim()} onClick={submit}>
          {saving ? '…' : '+ Add media'}
        </button>
      </div>

      {/* Members-разборы — отдельным списком, иначе после добавления их негде увидеть/удалить. */}
      {media.membersMedia.length > 0 && (
        <div className="flex flex-col gap-1">
          <p className="m-0 text-[11.5px] font-extrabold uppercase tracking-[0.12em] text-[var(--t-accent-dim)]">
            🔒 Members reviews
          </p>
          <ul className="m-0 flex list-none flex-col gap-2 p-0">
            {media.membersMedia.map((m) => (
              <MediaListRow
                key={m.id}
                item={{ id: m.id, media_type: m.media_type, source_type: m.source_type, url: m.url,
                  caption: [m.swimmer_name, m.caption].filter(Boolean).join(' · ') || m.caption }}
                onRemove={() => media.removeMedia(m.id)}
              />
            ))}
          </ul>
        </div>
      )}
      {error && <p className="text-[12.5px] font-bold text-[var(--t-danger)]">{error}</p>}
    </div>
  );
}
