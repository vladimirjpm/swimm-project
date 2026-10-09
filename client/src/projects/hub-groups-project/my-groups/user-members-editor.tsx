import { useState } from 'react';
import { useMyHubGroupEdit } from '../use-my-hub-groups';
import { inputCls, btnCls, btnDangerCls } from './my-groups-shared';

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
        <ul className="absolute z-10 m-0 mt-1 flex max-h-[220px] w-full list-none flex-col gap-1 overflow-y-auto rounded-[10px] border border-[var(--t-accent-border)] bg-[var(--t-surface-strong)] p-2">
          {results.map((s) => (
            <li key={s.id}>
              <button type="button"
                className="w-full cursor-pointer rounded-[6px] border-none bg-transparent px-2 py-1 text-left text-[12.5px] text-[var(--t-text)] hover:bg-[var(--t-accent-soft)]"
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

export function UserMembersEditor({ hubGroupId }: { hubGroupId: number }) {
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
      <p className="text-[11.5px] italic text-[var(--t-text-3)]">
        Private list — visible only to the group owner and admins, never on the public page.
      </p>
      <ul className="m-0 flex list-none flex-col gap-2 p-0">
        {members.map((m) => (
          <li key={m.userId} className="flex flex-col gap-1">
            <div className="flex items-center justify-between gap-2">
              <span className="min-w-0 truncate text-[13px] text-[var(--t-text)]">
                {m.displayName} <span className="text-[var(--t-text-3)]">({m.email})</span>
                {m.selfJoined && <span className="ml-1 text-[10.5px] text-[var(--t-accent-dim)]">joined on their own</span>}
                {m.status === 'pending' && (
                  <span className="ml-1 rounded-[6px] border border-[var(--t-warn-border)] px-[5px] py-[1px] text-[10px] font-extrabold text-[var(--t-warn)]">
                    request
                  </span>
                )}
                {m.swimmerId != null && (
                  <span className="ml-1 text-[11px] text-[var(--t-text-2)]">
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
          <p className="text-[12.5px] text-[var(--t-text-2)]">No account members yet.</p>
        )}
      </ul>
      <div className="flex flex-col gap-2">
        <input className={inputCls} placeholder="User email" value={email}
          onChange={(e) => setEmail(e.target.value)} />
        <SwimmerPicker edit={edit} placeholder="Label: swimmer (optional)" onPick={setLabelSwimmer} />
        {labelSwimmer && (
          <div className="flex items-center gap-2 text-[12px] text-[var(--t-text-2)]">
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
      {error && <p className="text-[12.5px] font-bold text-[var(--t-danger)]">{error}</p>}
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
    <div className="ml-2 flex flex-col gap-2 border-l border-[var(--t-border)] pl-3">
      <SwimmerPicker edit={edit} placeholder="Swimmer" onPick={setSwimmer} />
      {swimmer && (
        <div className="flex items-center gap-2 text-[12px] text-[var(--t-text-2)]">
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
      {error && <p className="text-[12.5px] font-bold text-[var(--t-danger)]">{error}</p>}
    </div>
  );
}
