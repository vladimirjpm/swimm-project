import { useState } from 'react';
import { useClubOptions, useMyHubGroupEdit } from '../use-my-hub-groups';
import { inputCls, btnCls } from './my-groups-shared';

export function ClubRequestPanel({ hubGroupId }: { hubGroupId: number }) {
  const edit = useMyHubGroupEdit(hubGroupId);
  const clubs = useClubOptions();
  const [clubId, setClubId] = useState<number | ''>('');
  const [message, setMessage] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  const request = edit.clubRequest;
  if (request?.status === 'pending') {
    return (
      <p className="text-[12.5px] text-[var(--t-text-2)]">
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
        <p className="text-[12.5px] text-[var(--t-text-2)]">
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
      {error && <p className="text-[12.5px] font-bold text-[var(--t-danger)]">{error}</p>}
    </div>
  );
}
