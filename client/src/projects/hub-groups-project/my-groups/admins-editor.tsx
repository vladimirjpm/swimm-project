import { useState } from 'react';
import { useMyHubGroupEdit } from '../use-my-hub-groups';
import { inputCls, btnCls, btnDangerCls } from './my-groups-shared';

export function AdminsEditor({ hubGroupId, isOwnerOrAdmin }: { hubGroupId: number; isOwnerOrAdmin: boolean }) {
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
            <span className="min-w-0 truncate text-[13px] text-[var(--t-text)]">{m.displayName} <span className="text-[var(--t-text-3)]">({m.email})</span></span>
            <button type="button" className={btnDangerCls} onClick={() => edit.removeAdmin(m.userId)}>✕</button>
          </li>
        ))}
        {edit.admins.length === 0 && (
          <p className="text-[12.5px] text-[var(--t-text-2)]">No group admins yet.</p>
        )}
      </ul>
      <div className="flex gap-2">
        <input className={inputCls} placeholder="User email" value={email}
          onChange={(e) => setEmail(e.target.value)} />
        <button type="button" className={btnCls} disabled={!email.trim()} onClick={submit}>+ Admin</button>
      </div>
      {error && <p className="text-[12.5px] font-bold text-[var(--t-danger)]">{error}</p>}
    </div>
  );
}
