import React, { useState } from 'react';
import { useClubOptions, useCurrentIdentity, useHubGroupMedia, useMyHubGroupEdit, useMyHubGroups } from './use-my-hub-groups';
import type { HubGroupInput, HubGroupLinkInput, HubGroupMediaInput, MyHubGroupRow } from './my-groups-types';
import type { HubGroupMediaItem } from '../../utils/interfaces/results';
import { routes } from '../../utils/routes';

const DISCLAIMER =
  'The roster is maintained by the group creator and is not an official club or federation entry.';

const cardCls =
  'hp-card-std rounded-[18px] border border-[#7dd3fc]/[0.22] p-[18px] shadow-[0_24px_60px_rgba(2,10,24,0.5)] backdrop-blur-[14px] lg:rounded-[24px] lg:p-[26px]';
const inputCls =
  'w-full rounded-[10px] border border-[#7dd3fc]/30 bg-[rgba(6,20,36,0.6)] px-3 py-2 text-[13px] text-[#f3f8fd] outline-none placeholder:text-[#cbe0f0]/40 focus:border-[#7dd3fc]';
const btnCls =
  'cursor-pointer rounded-[9px] border border-[#7dd3fc]/50 bg-transparent px-3 py-[7px] text-[12px] font-extrabold text-[#7dd3fc] transition-colors hover:bg-[rgba(56,189,248,0.14)] disabled:cursor-not-allowed disabled:opacity-40';
const btnDangerCls =
  'cursor-pointer rounded-[9px] border border-[#ef5350]/50 bg-transparent px-3 py-[7px] text-[12px] font-extrabold text-[#ef5350] transition-colors hover:bg-[rgba(239,83,80,0.14)] disabled:cursor-not-allowed disabled:opacity-40';

const EMPTY_INPUT: HubGroupInput = {
  name: '', nameEn: '', slug: '', description: '', iconUrl: '', coverImageUrl: '', location: '',
  country: '', clubId: null, isPublic: true, joinPolicy: 'open', links: [],
};

function GroupInputForm({
  initial, onSubmit, onCancel, submitLabel, recommendApproval,
}: {
  initial: HubGroupInput;
  onSubmit: (input: HubGroupInput) => Promise<{ success: boolean; error?: string | null }>;
  onCancel: () => void;
  submitLabel: string;
  /** Подсказка (не форс): официальной группе с members-контентом лучше режим заявок. */
  recommendApproval?: boolean;
}) {
  const [form, setForm] = useState<HubGroupInput>(initial);
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  const setField = <K extends keyof HubGroupInput>(key: K, value: HubGroupInput[K]) =>
    setForm((f) => ({ ...f, [key]: value }));

  const setLink = (i: number, field: keyof HubGroupLinkInput, value: string) =>
    setForm((f) => {
      const links = [...f.links];
      links[i] = { ...links[i], [field]: value };
      return { ...f, links };
    });

  const addLink = () => setForm((f) => ({ ...f, links: [...f.links, { kind: 'whatsapp', url: '' }] }));
  const removeLink = (i: number) => setForm((f) => ({ ...f, links: f.links.filter((_, idx) => idx !== i) }));

  const submit = async () => {
    setSaving(true);
    setError(null);
    const result = await onSubmit(form);
    setSaving(false);
    if (!result.success) setError(result.error ?? 'Could not save');
  };

  return (
    <div className="flex flex-col gap-3">
      <input className={inputCls} placeholder="Name" value={form.name}
        onChange={(e) => setField('name', e.target.value)} />
      <input className={inputCls} placeholder="Name (English)" value={form.nameEn ?? ''}
        onChange={(e) => setField('nameEn', e.target.value)} />
      <textarea className={inputCls} placeholder="Description" rows={2} value={form.description ?? ''}
        onChange={(e) => setField('description', e.target.value)} />
      <input className={inputCls} placeholder="City / pool" value={form.location ?? ''}
        onChange={(e) => setField('location', e.target.value)} />
      {/* Alpha-3 код World Aquatics (ISR/GER/…) — как в БД; резолв FK делает сервер. */}
      <input className={inputCls} placeholder="Country code (ISR)" maxLength={3} value={form.country ?? ''}
        onChange={(e) => setField('country', e.target.value.toUpperCase())} />
      <input className={inputCls} placeholder="Icon (link)" value={form.iconUrl ?? ''}
        onChange={(e) => setField('iconUrl', e.target.value)} />

      <div className="flex flex-col gap-2">
        {form.links.map((l, i) => (
          <div key={i} className="flex gap-2">
            <select className={`${inputCls} max-w-[130px]`} value={l.kind}
              onChange={(e) => setLink(i, 'kind', e.target.value)}>
              <option value="whatsapp">WhatsApp</option>
              <option value="telegram">Telegram</option>
              <option value="instagram">Instagram</option>
              <option value="site">Site</option>
            </select>
            <input className={inputCls} placeholder="URL" value={l.url}
              onChange={(e) => setLink(i, 'url', e.target.value)} />
            <button type="button" className={btnDangerCls} onClick={() => removeLink(i)}>✕</button>
          </div>
        ))}
        <button type="button" className={btnCls} onClick={addLink}>+ Link</button>
      </div>

      <label className="flex items-center gap-2 text-[13px] font-bold text-[#cbe0f0]/80">
        <input type="checkbox" checked={form.isPublic}
          onChange={(e) => setField('isPublic', e.target.checked)} />
        Public group
      </label>

      <label className="flex items-center gap-2 text-[13px] font-bold text-[#cbe0f0]/80">
        Joining:
        <select className={`${inputCls} max-w-[220px]`} value={form.joinPolicy ?? 'open'}
          onChange={(e) => setField('joinPolicy', e.target.value as 'open' | 'approval')}>
          <option value="open">open</option>
          <option value="approval">by request (coach approves)</option>
        </select>
      </label>
      {recommendApproval && (form.joinPolicy ?? 'open') === 'open' && (
        <p className="m-0 text-[11.5px] italic text-[#ffca7a]/80">
          In an official group, members-only content (reviews, training) is visible to everyone who
          joins — we recommend the “by request” mode.
        </p>
      )}

      {error && <p className="text-[12.5px] font-bold text-[#ef5350]">{error}</p>}

      <div className="flex gap-2">
        <button type="button" className={btnCls} disabled={saving || !form.name.trim()} onClick={submit}>
          {saving ? '…' : submitLabel}
        </button>
        <button type="button" className={btnCls} onClick={onCancel}>Cancel</button>
      </div>
    </div>
  );
}

function MembersEditor({ hubGroupId, clubId }: { hubGroupId: number; clubId?: number | null }) {
  const edit = useMyHubGroupEdit(hubGroupId);
  const [query, setQuery] = useState('');
  const [results, setResults] = useState<Awaited<ReturnType<typeof edit.searchSwimmers>>>([]);
  const [clubSwimmers, setClubSwimmers] = useState<Awaited<ReturnType<typeof edit.getClubSwimmers>> | null>(null);

  if (!edit.data) return null;

  const onSearch = async (q: string) => {
    setQuery(q);
    setResults(q.trim().length > 0 ? await edit.searchSwimmers(q) : []);
  };

  const toggleClubSwimmers = async () => {
    if (clubSwimmers != null) { setClubSwimmers(null); return; }
    if (clubId) setClubSwimmers(await edit.getClubSwimmers(clubId));
  };

  return (
    <div className="flex flex-col gap-2">
      <ul className="m-0 flex list-none flex-col gap-2 p-0">
        {edit.data.members.map((m) => (
          <li key={m.id} className="flex items-center justify-between gap-2">
            <span className="min-w-0 truncate text-[13px] text-[#f3f8fd]">{m.swimmerName || m.swimmerNameEn}</span>
            <div className="flex shrink-0 items-center gap-2">
              <select className={`${inputCls} w-[110px]`} value={m.role}
                onChange={(e) => edit.updateMember(m.id, e.target.value, m.sortOrder)}>
                <option value="member">member</option>
                <option value="captain">captain</option>
                <option value="coach">coach</option>
              </select>
              <button type="button" className={btnDangerCls} onClick={() => edit.removeMember(m.id)}>✕</button>
            </div>
          </li>
        ))}
        {edit.data.members.length === 0 && (
          <p className="text-[12.5px] text-[#cbe0f0]/55">The roster is empty.</p>
        )}
      </ul>

      <div className="relative">
        <input className={inputCls} placeholder="Find a swimmer by last name…" value={query}
          onChange={(e) => onSearch(e.target.value)} />
        {results.length > 0 && (
          <ul className="absolute z-10 m-0 mt-1 flex max-h-[220px] w-full list-none flex-col gap-1 overflow-y-auto rounded-[10px] border border-[#7dd3fc]/30 bg-[#06182c] p-2">
            {results.map((s) => (
              <li key={s.id}>
                <button type="button"
                  className="w-full cursor-pointer rounded-[6px] border-none bg-transparent px-2 py-1 text-left text-[12.5px] text-[#f3f8fd] hover:bg-[rgba(56,189,248,0.14)]"
                  onClick={async () => { await edit.addMember(s.id, 'member'); setQuery(''); setResults([]); }}>
                  {s.name || s.nameEn} {s.birthYear > 0 ? `(${s.birthYear})` : ''}
                </button>
              </li>
            ))}
          </ul>
        )}
      </div>

      {clubId && (
        <div>
          <button type="button" className={btnCls} onClick={toggleClubSwimmers}>
            {clubSwimmers != null ? 'Hide club swimmers' : 'Show club swimmers'}
          </button>
          {clubSwimmers != null && (
            <ul className="m-0 mt-2 flex max-h-[220px] list-none flex-col gap-1 overflow-y-auto rounded-[10px] border border-[#7dd3fc]/30 bg-[#06182c] p-2">
              {clubSwimmers.length === 0 && (
                <p className="px-2 py-1 text-[12.5px] text-[#cbe0f0]/55">No club swimmers found.</p>
              )}
              {clubSwimmers.map((s) => (
                <li key={s.id}>
                  <button type="button"
                    className="w-full cursor-pointer rounded-[6px] border-none bg-transparent px-2 py-1 text-left text-[12.5px] text-[#f3f8fd] hover:bg-[rgba(56,189,248,0.14)]"
                    onClick={() => edit.addMember(s.id, 'member')}>
                    {s.name || s.nameEn} {s.birthYear > 0 ? `(${s.birthYear})` : ''}
                  </button>
                </li>
              ))}
            </ul>
          )}
        </div>
      )}
    </div>
  );
}

function AdminsEditor({ hubGroupId, isOwnerOrAdmin }: { hubGroupId: number; isOwnerOrAdmin: boolean }) {
  const edit = useMyHubGroupEdit(hubGroupId);
  const [email, setEmail] = useState('');
  const [error, setError] = useState<string | null>(null);

  if (!isOwnerOrAdmin) return null;

  const submit = async () => {
    setError(null);
    const result = await edit.addAdmin(email);
    if (result.success) setEmail(''); else setError(result.error ?? 'Something went wrong');
  };

  return (
    <div className="flex flex-col gap-2">
      <ul className="m-0 flex list-none flex-col gap-2 p-0">
        {edit.admins.map((m) => (
          <li key={m.userId} className="flex items-center justify-between gap-2">
            <span className="min-w-0 truncate text-[13px] text-[#f3f8fd]">{m.displayName} <span className="text-[#cbe0f0]/50">({m.email})</span></span>
            <button type="button" className={btnDangerCls} onClick={() => edit.removeAdmin(m.userId)}>✕</button>
          </li>
        ))}
        {edit.admins.length === 0 && (
          <p className="text-[12.5px] text-[#cbe0f0]/55">No group admins yet.</p>
        )}
      </ul>
      <div className="flex gap-2">
        <input className={inputCls} placeholder="User email" value={email}
          onChange={(e) => setEmail(e.target.value)} />
        <button type="button" className={btnCls} disabled={!email.trim()} onClick={submit}>+ Admin</button>
      </div>
      {error && <p className="text-[12.5px] font-bold text-[#ef5350]">{error}</p>}
    </div>
  );
}

/** Инлайн-поиск пловца с выбором из выпадающего списка — переиспользуется для ярлыка «родитель». */
function SwimmerPicker({
  edit, placeholder, onPick,
}: {
  edit: ReturnType<typeof useMyHubGroupEdit>;
  placeholder: string;
  onPick: (s: { id: number; name: string; nameEn: string }) => void;
}) {
  const [query, setQuery] = useState('');
  const [results, setResults] = useState<Awaited<ReturnType<typeof edit.searchSwimmers>>>([]);

  const onSearch = async (q: string) => {
    setQuery(q);
    setResults(q.trim().length > 0 ? await edit.searchSwimmers(q) : []);
  };

  return (
    <div className="relative">
      <input className={inputCls} placeholder={placeholder} value={query}
        onChange={(e) => onSearch(e.target.value)} />
      {results.length > 0 && (
        <ul className="absolute z-10 m-0 mt-1 flex max-h-[220px] w-full list-none flex-col gap-1 overflow-y-auto rounded-[10px] border border-[#7dd3fc]/30 bg-[#06182c] p-2">
          {results.map((s) => (
            <li key={s.id}>
              <button type="button"
                className="w-full cursor-pointer rounded-[6px] border-none bg-transparent px-2 py-1 text-left text-[12.5px] text-[#f3f8fd] hover:bg-[rgba(56,189,248,0.14)]"
                onClick={() => { onPick(s); setQuery(''); setResults([]); }}>
                {s.name || s.nameEn} {s.birthYear > 0 ? `(${s.birthYear})` : ''}
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

function UserMembersEditor({ hubGroupId }: { hubGroupId: number }) {
  const edit = useMyHubGroupEdit(hubGroupId);
  const [email, setEmail] = useState('');
  const [labelSwimmer, setLabelSwimmer] = useState<{ id: number; name: string; nameEn: string } | null>(null);
  const [note, setNote] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [editingUserId, setEditingUserId] = useState<number | null>(null);

  const submit = async () => {
    setError(null);
    const result = await edit.addUserMember(email, labelSwimmer?.id ?? null, note || null);
    if (result.success) { setEmail(''); setLabelSwimmer(null); setNote(''); } else setError(result.error ?? 'Something went wrong');
  };

  const members = edit.data?.userMembers ?? [];

  return (
    <div className="flex flex-col gap-2">
      <p className="text-[11.5px] italic text-[#cbe0f0]/45">
        Private list — visible only to the group owner and admins, never on the public page.
      </p>
      <ul className="m-0 flex list-none flex-col gap-2 p-0">
        {members.map((m) => (
          <li key={m.userId} className="flex flex-col gap-1">
            <div className="flex items-center justify-between gap-2">
              <span className="min-w-0 truncate text-[13px] text-[#f3f8fd]">
                {m.displayName} <span className="text-[#cbe0f0]/50">({m.email})</span>
                {m.selfJoined && <span className="ml-1 text-[10.5px] text-[#7dd3fc]/70">joined on their own</span>}
                {m.status === 'pending' && (
                  <span className="ml-1 rounded-[6px] border border-[#ffca7a]/50 px-[5px] py-[1px] text-[10px] font-extrabold text-[#ffca7a]">
                    request
                  </span>
                )}
                {m.swimmerId != null && (
                  <span className="ml-1 text-[11px] text-[#cbe0f0]/70">
                    — {m.note || 'parent'}: {m.swimmerName}
                  </span>
                )}
              </span>
              <div className="flex shrink-0 items-center gap-2">
                {m.status === 'pending' && (
                  <button type="button" className={btnCls} onClick={() => edit.approveUserMember(m.userId)}>
                    Approve
                  </button>
                )}
                <button type="button" className={btnCls}
                  onClick={() => setEditingUserId(editingUserId === m.userId ? null : m.userId)}>
                  {m.swimmerId != null ? 'Label' : '+ Label'}
                </button>
                <button type="button" className={btnDangerCls} onClick={() => edit.removeUserMember(m.userId)}>✕</button>
              </div>
            </div>
            {editingUserId === m.userId && (
              <UserMemberLabelEditor edit={edit} member={m} onDone={() => setEditingUserId(null)} />
            )}
          </li>
        ))}
        {members.length === 0 && (
          <p className="text-[12.5px] text-[#cbe0f0]/55">No account members yet.</p>
        )}
      </ul>
      <div className="flex flex-col gap-2">
        <input className={inputCls} placeholder="User email" value={email}
          onChange={(e) => setEmail(e.target.value)} />
        <SwimmerPicker edit={edit} placeholder="Label: swimmer (optional)" onPick={setLabelSwimmer} />
        {labelSwimmer && (
          <div className="flex items-center gap-2 text-[12px] text-[#cbe0f0]/80">
            <span>Swimmer: {labelSwimmer.name || labelSwimmer.nameEn}</span>
            <button type="button" className={btnDangerCls} onClick={() => setLabelSwimmer(null)}>✕</button>
          </div>
        )}
        {labelSwimmer && (
          <input className={inputCls} placeholder="Note (e.g. “parent”)" value={note}
            onChange={(e) => setNote(e.target.value)} />
        )}
        <button type="button" className={btnCls} disabled={!email.trim()} onClick={submit}>+ Member</button>
      </div>
      {error && <p className="text-[12.5px] font-bold text-[#ef5350]">{error}</p>}
    </div>
  );
}

/** Инлайн-редактор ярлыка «родитель» для уже существующего участника-аккаунта. */
function UserMemberLabelEditor({
  edit, member, onDone,
}: {
  edit: ReturnType<typeof useMyHubGroupEdit>;
  member: { userId: number; swimmerId?: number | null; note?: string | null };
  onDone: () => void;
}) {
  const [swimmer, setSwimmer] = useState<{ id: number; name: string; nameEn: string } | null>(
    member.swimmerId != null ? { id: member.swimmerId, name: '', nameEn: '' } : null,
  );
  const [note, setNote] = useState(member.note ?? '');
  const [error, setError] = useState<string | null>(null);

  const save = async () => {
    setError(null);
    const result = await edit.setUserMemberLabel(member.userId, swimmer?.id ?? null, note || null);
    if (result.success) onDone(); else setError(result.error ?? 'Something went wrong');
  };

  const clear = async () => {
    setError(null);
    const result = await edit.setUserMemberLabel(member.userId, null, null);
    if (result.success) onDone(); else setError(result.error ?? 'Something went wrong');
  };

  return (
    <div className="ml-2 flex flex-col gap-2 border-l border-[#7dd3fc]/25 pl-3">
      <SwimmerPicker edit={edit} placeholder="Swimmer" onPick={setSwimmer} />
      {swimmer && (
        <div className="flex items-center gap-2 text-[12px] text-[#cbe0f0]/80">
          <span>{swimmer.name || swimmer.nameEn || `#${swimmer.id}`}</span>
          <button type="button" className={btnDangerCls} onClick={() => setSwimmer(null)}>✕</button>
        </div>
      )}
      <input className={inputCls} placeholder="Note (e.g. “parent”)" value={note}
        onChange={(e) => setNote(e.target.value)} />
      <div className="flex gap-2">
        <button type="button" className={btnCls} onClick={save}>Save</button>
        <button type="button" className={btnCls} onClick={clear}>Clear label</button>
        <button type="button" className={btnCls} onClick={onDone}>Cancel</button>
      </div>
      {error && <p className="text-[12.5px] font-bold text-[#ef5350]">{error}</p>}
    </div>
  );
}

const EMPTY_MEDIA_INPUT: HubGroupMediaInput = {
  mediaType: 'image', sourceType: 'other', url: '', caption: '', trainingId: null,
};

/** Строка списка медиа (галерея или тренировка) с кнопкой удаления. */
function MediaListRow({ item, onRemove }: { item: HubGroupMediaItem; onRemove: () => void }) {
  return (
    <li className="flex items-center justify-between gap-2">
      <span className="min-w-0 truncate text-[13px] text-[#f3f8fd]">
        {item.media_type === 'album' ? '📂' : item.media_type === 'video' ? '🎬' : '🖼️'} {item.caption || item.url}
      </span>
      <button type="button" className={btnDangerCls} onClick={onRemove}>✕</button>
    </li>
  );
}

/** Редактор медиа: публичная галерея группы + медиа тренировок (владелец/админ). */
function MediaEditor({ hubGroupId, slug }: { hubGroupId: number; slug: string }) {
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
      <p className="text-[11.5px] italic text-[#cbe0f0]/45">
        No training set = public gallery (visible on the group page). Pick a training to attach media to it instead.
      </p>

      {media.loadError && (
        <p className="text-[12.5px] font-bold text-[#ef5350]">{media.loadError}</p>
      )}

      <ul className="m-0 flex list-none flex-col gap-2 p-0">
        {media.gallery.map((m) => (
          <MediaListRow key={m.id} item={m} onRemove={() => media.removeMedia(m.id)} />
        ))}
        {media.gallery.length === 0 && !media.loadError && (
          <p className="text-[12.5px] text-[#cbe0f0]/55">No public gallery items yet.</p>
        )}
      </ul>

      {/* Медиа, привязанное к тренировкам, — иначе после добавления его негде увидеть/удалить. */}
      {media.trainings.filter((t) => t.media.length > 0).map((t) => (
        <div key={t.sessionId} className="flex flex-col gap-1">
          <p className="m-0 text-[11.5px] font-extrabold uppercase tracking-[0.12em] text-[#7dd3fc]/70">
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
          <p className="m-0 text-[11.5px] font-extrabold uppercase tracking-[0.12em] text-[#7dd3fc]/70">
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
      {error && <p className="text-[12.5px] font-bold text-[#ef5350]">{error}</p>}
    </div>
  );
}

function ClubRequestPanel({ hubGroupId }: { hubGroupId: number }) {
  const edit = useMyHubGroupEdit(hubGroupId);
  const clubs = useClubOptions();
  const [clubId, setClubId] = useState<number | ''>('');
  const [message, setMessage] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  const request = edit.clubRequest;
  if (request?.status === 'pending') {
    return (
      <p className="text-[12.5px] text-[#cbe0f0]/70">
        The official-status request for “{request.clubName}” is under review.
      </p>
    );
  }

  const submit = async () => {
    if (!clubId) return;
    setSaving(true);
    setError(null);
    const r = await edit.submitClubRequest({ clubId, message: message.trim() || null });
    setSaving(false);
    if (!r.success) setError(r.error ?? 'Could not submit the request');
  };

  return (
    <div className="flex flex-col gap-2">
      {request?.status === 'rejected' && (
        <p className="text-[12.5px] text-[#cbe0f0]/55">
          The previous request for “{request.clubName}” was declined — you can apply again.
        </p>
      )}
      <div className="flex gap-2">
        <select className={`${inputCls} max-w-[220px]`} value={clubId}
          onChange={(e) => setClubId(e.target.value ? Number(e.target.value) : '')}>
          <option value="">Select a club…</option>
          {clubs.map((c) => (
            <option key={c.id} value={c.id}>{c.name}</option>
          ))}
        </select>
        <input className={inputCls} placeholder="Message to the admin (optional)" value={message}
          onChange={(e) => setMessage(e.target.value)} />
        <button type="button" className={btnCls} disabled={!clubId || saving} onClick={submit}>
          {saving ? '…' : 'Submit request'}
        </button>
      </div>
      {error && <p className="text-[12.5px] font-bold text-[#ef5350]">{error}</p>}
    </div>
  );
}

function EditGroupCard({ row, currentUserId, isAdmin, onClose, onSaved }: {
  row: MyHubGroupRow; currentUserId: number | null; isAdmin: boolean; onClose: () => void; onSaved: () => void;
}) {
  const edit = useMyHubGroupEdit(row.id);

  if (edit.forbidden) return <p className="text-[13px] font-bold text-[#ef5350]">You do not have rights to edit this group.</p>;
  if (edit.loading || !edit.data) return <p className="text-[13px] text-[#cbe0f0]/60">Loading…</p>;

  const isOwnerOrAdmin = isAdmin || edit.data.ownerUserId === currentUserId;
  const input: HubGroupInput = {
    name: edit.data.name, nameEn: edit.data.nameEn, slug: edit.data.slug, description: edit.data.description,
    iconUrl: edit.data.iconUrl, coverImageUrl: edit.data.coverImageUrl, location: edit.data.location,
    // "" (не null): пустое поле формы = явное «снять страну» на сервере.
    country: edit.data.country ?? '',
    clubId: edit.data.clubId, isPublic: edit.data.isPublic,
    joinPolicy: edit.data.joinPolicy, links: edit.data.links,
  };

  return (
    <div className="flex flex-col gap-4">
      <GroupInputForm
        initial={input}
        recommendApproval={edit.data.isOfficial}
        submitLabel="Save"
        onCancel={onClose}
        onSubmit={async (i) => { const r = await edit.update(i); if (r.success) onSaved(); return r; }}
      />
      <div>
        <h3 className="mb-2 text-[12px] font-black uppercase tracking-[0.18em] text-[#7dd3fc]">Members</h3>
        <MembersEditor hubGroupId={row.id} clubId={edit.data.isOfficial ? edit.data.clubId : null} />
        <p className="mt-2 text-[11.5px] italic text-[#cbe0f0]/45">{DISCLAIMER}</p>
      </div>
      <div>
        <h3 className="mb-2 text-[12px] font-black uppercase tracking-[0.18em] text-[#7dd3fc]">Group admins</h3>
        <AdminsEditor hubGroupId={row.id} isOwnerOrAdmin={isOwnerOrAdmin} />
      </div>
      <div>
        <h3 className="mb-2 text-[12px] font-black uppercase tracking-[0.18em] text-[#7dd3fc]">Account members</h3>
        <UserMembersEditor hubGroupId={row.id} />
      </div>
      <div>
        <h3 className="mb-2 text-[12px] font-black uppercase tracking-[0.18em] text-[#7dd3fc]">Gallery</h3>
        <MediaEditor hubGroupId={row.id} slug={row.slug} />
      </div>
      {isOwnerOrAdmin && !edit.data.isOfficial && (
        <div>
          <h3 className="mb-2 text-[12px] font-black uppercase tracking-[0.18em] text-[#7dd3fc]">
            Official club status
          </h3>
          <ClubRequestPanel hubGroupId={row.id} />
        </div>
      )}
      {edit.data.isOfficial && (
        <p className="text-[12.5px] font-bold text-[#38ef8f]">Official Group of {row.clubName}</p>
      )}
    </div>
  );
}

/** Панель самообслуживания групп (8.6) — на странице списка групп, только для авторизованных. */
export default function MyGroupsPanel() {
  const identity = useCurrentIdentity();
  const mine = useMyHubGroups(identity.isAuthenticated);
  const [creating, setCreating] = useState(false);
  const [editingId, setEditingId] = useState<number | null>(null);

  if (identity.loading || !identity.isAuthenticated) return null;

  return (
    <section className="px-4 pt-[26px] lg:px-16" aria-label="My groups">
      <div className={cardCls}>
        <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
          <h2 className="text-[15px] font-black uppercase tracking-[0.2em] text-[#7dd3fc]">My groups</h2>
          {!creating && mine.eligibility?.canCreate && (
            <button type="button" className={btnCls} onClick={() => setCreating(true)}>
              + Create group{mine.eligibility.remaining != null ? ` (${mine.eligibility.remaining} left)` : ''}
            </button>
          )}
        </div>

        {!creating && !mine.eligibility?.canCreate && mine.eligibility?.reason && mine.groups.length === 0 && (
          <p className="text-[12.5px] text-[#cbe0f0]/55">{mine.eligibility.reason}</p>
        )}

        {creating && (
          <div className="mb-4">
            <GroupInputForm
              initial={EMPTY_INPUT}
              submitLabel="Create"
              onCancel={() => setCreating(false)}
              onSubmit={async (input) => { const r = await mine.createGroup(input); if (r.success) setCreating(false); return r; }}
            />
          </div>
        )}

        {mine.loading ? (
          <p className="text-[13px] text-[#cbe0f0]/60">Loading…</p>
        ) : mine.groups.length === 0 && !creating ? (
          <p className="text-[13px] text-[#cbe0f0]/60">You do not own or co-coach any group yet.</p>
        ) : (
          <ul className="m-0 flex list-none flex-col gap-3 p-0">
            {mine.groups.map((g) => (
              <li key={g.id}>
                <div className="flex items-center justify-between gap-3">
                  <div className="min-w-0">
                    <a href={routes.group(g.slug)}
                      className="truncate text-[14px] font-extrabold text-[#f3f8fd] no-underline hover:underline">
                      {g.name}
                    </a>
                    {g.isOfficial && (
                      <span className="hp-mono ml-2 rounded-[6px] border border-[#38ef8f]/40 px-[6px] py-[2px] text-[10px] font-extrabold text-[#38ef8f]">
                        Official · {g.clubName}
                      </span>
                    )}
                    <div className="text-[11.5px] text-[#cbe0f0]/55">{g.memberCount} · swimmers</div>
                  </div>
                  <div className="flex shrink-0 gap-2">
                    <button type="button" className={btnCls}
                      onClick={() => setEditingId(editingId === g.id ? null : g.id)}>
                      {editingId === g.id ? 'Close' : 'Edit'}
                    </button>
                    <button type="button" className={btnDangerCls}
                      onClick={async () => { if (window.confirm(`Delete the group “${g.name}”?`)) await mine.deleteGroup(g.id); }}>
                      Delete
                    </button>
                  </div>
                </div>
                {editingId === g.id && (
                  <div className="mt-3 border-t border-[#7dd3fc]/15 pt-3">
                    <EditGroupCard
                      row={g}
                      currentUserId={identity.userId}
                      isAdmin={identity.isAdmin}
                      onClose={() => setEditingId(null)}
                      onSaved={() => mine.reload()}
                    />
                  </div>
                )}
              </li>
            ))}
          </ul>
        )}
      </div>
    </section>
  );
}
