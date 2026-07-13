import React, { useState } from 'react';
import { useClubOptions, useCurrentIdentity, useHubGroupMedia, useMyHubGroupEdit, useMyHubGroups } from './use-my-hub-groups';
import type { HubGroupInput, HubGroupLinkInput, HubGroupMediaInput, MyHubGroupRow } from './my-groups-types';
import type { HubGroupMediaItem } from '../../utils/interfaces/results';

const DISCLAIMER =
  'Состав ведётся создателем группы и не является официальной заявкой клуба или федерации.';

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
  country: '', clubId: null, isPublic: true, links: [],
};

function GroupInputForm({
  initial, onSubmit, onCancel, submitLabel,
}: {
  initial: HubGroupInput;
  onSubmit: (input: HubGroupInput) => Promise<{ success: boolean; error?: string | null }>;
  onCancel: () => void;
  submitLabel: string;
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
    if (!result.success) setError(result.error ?? 'Ошибка сохранения');
  };

  return (
    <div className="flex flex-col gap-3">
      <input className={inputCls} placeholder="Название" value={form.name}
        onChange={(e) => setField('name', e.target.value)} />
      <input className={inputCls} placeholder="Название (англ.)" value={form.nameEn ?? ''}
        onChange={(e) => setField('nameEn', e.target.value)} />
      <textarea className={inputCls} placeholder="Описание" rows={2} value={form.description ?? ''}
        onChange={(e) => setField('description', e.target.value)} />
      <input className={inputCls} placeholder="Город/бассейн" value={form.location ?? ''}
        onChange={(e) => setField('location', e.target.value)} />
      {/* Alpha-3 код World Aquatics (ISR/GER/…) — как в БД; резолв FK делает сервер. */}
      <input className={inputCls} placeholder="Страна — код (ISR)" maxLength={3} value={form.country ?? ''}
        onChange={(e) => setField('country', e.target.value.toUpperCase())} />
      <input className={inputCls} placeholder="Иконка (ссылка)" value={form.iconUrl ?? ''}
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
        <button type="button" className={btnCls} onClick={addLink}>+ Ссылка</button>
      </div>

      <label className="flex items-center gap-2 text-[13px] font-bold text-[#cbe0f0]/80">
        <input type="checkbox" checked={form.isPublic}
          onChange={(e) => setField('isPublic', e.target.checked)} />
        Публичная группа
      </label>

      {error && <p className="text-[12.5px] font-bold text-[#ef5350]">{error}</p>}

      <div className="flex gap-2">
        <button type="button" className={btnCls} disabled={saving || !form.name.trim()} onClick={submit}>
          {saving ? '…' : submitLabel}
        </button>
        <button type="button" className={btnCls} onClick={onCancel}>Отмена</button>
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
          <p className="text-[12.5px] text-[#cbe0f0]/55">Состав пока пуст.</p>
        )}
      </ul>

      <div className="relative">
        <input className={inputCls} placeholder="Найти пловца по фамилии…" value={query}
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
            {clubSwimmers != null ? 'Скрыть пловцов клуба' : 'Показать пловцов клуба'}
          </button>
          {clubSwimmers != null && (
            <ul className="m-0 mt-2 flex max-h-[220px] list-none flex-col gap-1 overflow-y-auto rounded-[10px] border border-[#7dd3fc]/30 bg-[#06182c] p-2">
              {clubSwimmers.length === 0 && (
                <p className="px-2 py-1 text-[12.5px] text-[#cbe0f0]/55">Пловцов клуба не найдено.</p>
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
    if (result.success) setEmail(''); else setError(result.error ?? 'Ошибка');
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
          <p className="text-[12.5px] text-[#cbe0f0]/55">Админов группы пока нет.</p>
        )}
      </ul>
      <div className="flex gap-2">
        <input className={inputCls} placeholder="Email пользователя" value={email}
          onChange={(e) => setEmail(e.target.value)} />
        <button type="button" className={btnCls} disabled={!email.trim()} onClick={submit}>+ Админ</button>
      </div>
      {error && <p className="text-[12.5px] font-bold text-[#ef5350]">{error}</p>}
    </div>
  );
}

function UserMembersEditor({ hubGroupId }: { hubGroupId: number }) {
  const edit = useMyHubGroupEdit(hubGroupId);
  const [email, setEmail] = useState('');
  const [error, setError] = useState<string | null>(null);

  const submit = async () => {
    setError(null);
    const result = await edit.addUserMember(email);
    if (result.success) setEmail(''); else setError(result.error ?? 'Ошибка');
  };

  const members = edit.data?.userMembers ?? [];

  return (
    <div className="flex flex-col gap-2">
      <p className="text-[11.5px] italic text-[#cbe0f0]/45">
        Приватный список — виден только владельцу и админам группы, не на публичной странице.
      </p>
      <ul className="m-0 flex list-none flex-col gap-2 p-0">
        {members.map((m) => (
          <li key={m.userId} className="flex items-center justify-between gap-2">
            <span className="min-w-0 truncate text-[13px] text-[#f3f8fd]">
              {m.displayName} <span className="text-[#cbe0f0]/50">({m.email})</span>
              {m.selfJoined && <span className="ml-1 text-[10.5px] text-[#7dd3fc]/70">вступил сам</span>}
            </span>
            <button type="button" className={btnDangerCls} onClick={() => edit.removeUserMember(m.userId)}>✕</button>
          </li>
        ))}
        {members.length === 0 && (
          <p className="text-[12.5px] text-[#cbe0f0]/55">Участников-аккаунтов пока нет.</p>
        )}
      </ul>
      <div className="flex gap-2">
        <input className={inputCls} placeholder="Email пользователя" value={email}
          onChange={(e) => setEmail(e.target.value)} />
        <button type="button" className={btnCls} disabled={!email.trim()} onClick={submit}>+ Участник</button>
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

  const setField = <K extends keyof HubGroupMediaInput>(key: K, value: HubGroupMediaInput[K]) =>
    setForm((f) => ({ ...f, [key]: value }));

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
    if (result.success) setForm(EMPTY_MEDIA_INPUT);
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
        <select className={`${inputCls} max-w-[220px]`} value={form.trainingId ?? ''}
          onChange={(e) => setField('trainingId', e.target.value ? Number(e.target.value) : null)}>
          <option value="">Public gallery</option>
          {media.trainings.map((t) => (
            <option key={t.sessionId} value={t.sessionId}>{t.label}</option>
          ))}
        </select>
        <button type="button" className={btnCls} disabled={saving || !form.url.trim()} onClick={submit}>
          {saving ? '…' : '+ Add media'}
        </button>
      </div>
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
        Заявка на официальный статус клуба «{request.clubName}» рассматривается.
      </p>
    );
  }

  const submit = async () => {
    if (!clubId) return;
    setSaving(true);
    setError(null);
    const r = await edit.submitClubRequest({ clubId, message: message.trim() || null });
    setSaving(false);
    if (!r.success) setError(r.error ?? 'Ошибка отправки заявки');
  };

  return (
    <div className="flex flex-col gap-2">
      {request?.status === 'rejected' && (
        <p className="text-[12.5px] text-[#cbe0f0]/55">
          Предыдущая заявка на «{request.clubName}» отклонена — можно подать снова.
        </p>
      )}
      <div className="flex gap-2">
        <select className={`${inputCls} max-w-[220px]`} value={clubId}
          onChange={(e) => setClubId(e.target.value ? Number(e.target.value) : '')}>
          <option value="">Выбрать клуб…</option>
          {clubs.map((c) => (
            <option key={c.id} value={c.id}>{c.name}</option>
          ))}
        </select>
        <input className={inputCls} placeholder="Сообщение админу (необязательно)" value={message}
          onChange={(e) => setMessage(e.target.value)} />
        <button type="button" className={btnCls} disabled={!clubId || saving} onClick={submit}>
          {saving ? '…' : 'Подать заявку'}
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

  if (edit.forbidden) return <p className="text-[13px] font-bold text-[#ef5350]">Нет прав на правку этой группы.</p>;
  if (edit.loading || !edit.data) return <p className="text-[13px] text-[#cbe0f0]/60">Loading…</p>;

  const isOwnerOrAdmin = isAdmin || edit.data.ownerUserId === currentUserId;
  const input: HubGroupInput = {
    name: edit.data.name, nameEn: edit.data.nameEn, slug: edit.data.slug, description: edit.data.description,
    iconUrl: edit.data.iconUrl, coverImageUrl: edit.data.coverImageUrl, location: edit.data.location,
    // "" (не null): пустое поле формы = явное «снять страну» на сервере.
    country: edit.data.country ?? '',
    clubId: edit.data.clubId, isPublic: edit.data.isPublic, links: edit.data.links,
  };

  return (
    <div className="flex flex-col gap-4">
      <GroupInputForm
        initial={input}
        submitLabel="Сохранить"
        onCancel={onClose}
        onSubmit={async (i) => { const r = await edit.update(i); if (r.success) onSaved(); return r; }}
      />
      <div>
        <h3 className="mb-2 text-[12px] font-black uppercase tracking-[0.18em] text-[#7dd3fc]">Участники</h3>
        <MembersEditor hubGroupId={row.id} clubId={edit.data.isOfficial ? edit.data.clubId : null} />
        <p className="mt-2 text-[11.5px] italic text-[#cbe0f0]/45">{DISCLAIMER}</p>
      </div>
      <div>
        <h3 className="mb-2 text-[12px] font-black uppercase tracking-[0.18em] text-[#7dd3fc]">Админы группы</h3>
        <AdminsEditor hubGroupId={row.id} isOwnerOrAdmin={isOwnerOrAdmin} />
      </div>
      <div>
        <h3 className="mb-2 text-[12px] font-black uppercase tracking-[0.18em] text-[#7dd3fc]">Участники-аккаунты</h3>
        <UserMembersEditor hubGroupId={row.id} />
      </div>
      <div>
        <h3 className="mb-2 text-[12px] font-black uppercase tracking-[0.18em] text-[#7dd3fc]">Gallery</h3>
        <MediaEditor hubGroupId={row.id} slug={row.slug} />
      </div>
      {isOwnerOrAdmin && !edit.data.isOfficial && (
        <div>
          <h3 className="mb-2 text-[12px] font-black uppercase tracking-[0.18em] text-[#7dd3fc]">
            Официальный статус клуба
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
          <h2 className="text-[15px] font-black uppercase tracking-[0.2em] text-[#7dd3fc]">Мои группы</h2>
          {!creating && mine.eligibility?.canCreate && (
            <button type="button" className={btnCls} onClick={() => setCreating(true)}>
              + Создать группу{mine.eligibility.remaining != null ? ` (осталось ${mine.eligibility.remaining})` : ''}
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
              submitLabel="Создать"
              onCancel={() => setCreating(false)}
              onSubmit={async (input) => { const r = await mine.createGroup(input); if (r.success) setCreating(false); return r; }}
            />
          </div>
        )}

        {mine.loading ? (
          <p className="text-[13px] text-[#cbe0f0]/60">Loading…</p>
        ) : mine.groups.length === 0 && !creating ? (
          <p className="text-[13px] text-[#cbe0f0]/60">Вы пока не владеете и не со-тренируете ни одной группы.</p>
        ) : (
          <ul className="m-0 flex list-none flex-col gap-3 p-0">
            {mine.groups.map((g) => (
              <li key={g.id}>
                <div className="flex items-center justify-between gap-3">
                  <div className="min-w-0">
                    <a href={`./groups.html?group=${encodeURIComponent(g.slug)}`}
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
                      {editingId === g.id ? 'Закрыть' : 'Править'}
                    </button>
                    <button type="button" className={btnDangerCls}
                      onClick={async () => { if (window.confirm(`Удалить группу «${g.name}»?`)) await mine.deleteGroup(g.id); }}>
                      Удалить
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
