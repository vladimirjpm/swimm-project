import React, { useCallback, useEffect, useState } from 'react';
import { routes } from '../../../utils/routes';
import { publicationsApiFetch } from './group-bits';

/**
 * Заявки тренеров — карточка «Coach requests» таба Admin (docs/plans/coach-access-request-plan.md,
 * Т2). Видит только тот, кто назначает админов (владелец, админ сайта): сервер отвечает остальным
 * 403, и карточка молча не рисуется — второй копии правила прав на клиенте нет.
 *
 * Approve — админ группы + участие active + метка на его «Me»-пловца (если тот в составе);
 * Decline — заявка снимается, участником он остаётся.
 */

export interface CoachRequestItem {
  userId: number;
  displayName: string;
  email: string;
  requestedAt: string;
  membershipStatus: 'active' | 'pending';
  meSwimmerId: number | null;
  meSwimmerName: string | null;
  meSwimmerInGroup: boolean;
}

const requestsUrl = (groupId: number) => `/api/me/hub-groups/${groupId}/coach-requests`;

/** Висящие заявки; 403/ошибка — пусто (не управляет админами — карточки и бейджа нет). */
export async function loadCoachRequests(groupId: number): Promise<CoachRequestItem[]> {
  try {
    const r = await fetch(requestsUrl(groupId), { credentials: 'include', cache: 'no-store' });
    return r.ok ? ((await r.json()) as CoachRequestItem[]) : [];
  } catch {
    return [];
  }
}

function GroupCoachRequestsCard({ groupId, reloadKey, onDecided }: {
  groupId: number;
  reloadKey: number;
  onDecided: () => void;
}) {
  const [items, setItems] = useState<CoachRequestItem[]>([]);
  const [busyId, setBusyId] = useState<number | null>(null);
  const [error, setError] = useState<string | null>(null);

  const reload = useCallback(async () => setItems(await loadCoachRequests(groupId)), [groupId]);
  useEffect(() => { void reload(); }, [reload, reloadKey]);

  if (items.length === 0) return null;

  const decide = async (userId: number, approve: boolean) => {
    setBusyId(userId);
    setError(null);
    try {
      const r = await publicationsApiFetch(`${requestsUrl(groupId)}/${userId}/${approve ? 'approve' : 'decline'}`, { method: 'POST' });
      if (!r.ok) {
        const body = await r.json().catch(() => null);
        setError((body && typeof body.error === 'string' && body.error) || 'Could not save — please try again.');
      }
      await reload();
      onDecided();
    } finally {
      setBusyId(null);
    }
  };

  return (
    <div id="coach-requests" className="deep-card" aria-label="Coach requests">
      <h2 className="deep-card-title mb-1">Coach requests</h2>
      <p className="m-0 mb-3 text-[12px] leading-snug text-[var(--t-text-3)]">
        Approve makes the person a group admin: they can edit the roster, schedule, levels and lane plans.
      </p>
      {error && <p className="m-0 mb-2 text-[12px] font-bold text-[var(--t-danger)]">{error}</p>}
      <div className="flex flex-col gap-2">
        {items.map((item) => (
          <div key={item.userId} className="flex flex-wrap items-center gap-3 rounded-[10px] border border-[var(--t-border)] p-2">
            <div className="min-w-0 flex-1">
              <p className="m-0 truncate text-[13px] font-extrabold text-[var(--t-text)]">{item.displayName || item.email}</p>
              <p className="m-0 truncate text-[11px] text-[var(--t-text-3)]">{item.email}</p>
              <p className="m-0 text-[11.5px] text-[var(--t-text-2)]">
                {item.meSwimmerId != null && item.meSwimmerName ? (
                  <>
                    Me:{' '}
                    <a href={routes.swimmer(item.meSwimmerId)} dir="auto" className="font-bold text-[var(--t-accent)] no-underline hover:underline">
                      {item.meSwimmerName}
                    </a>
                    {item.meSwimmerInGroup ? ' · in the roster' : ' · not in the roster'}
                  </>
                ) : 'No “Me” swimmer marked'}
                {item.membershipStatus === 'pending' && ' · approving also lets them join'}
              </p>
            </div>
            <div className="flex shrink-0 gap-2">
              <button
                type="button"
                disabled={busyId === item.userId}
                onClick={() => { void decide(item.userId, true); }}
                className="hp-mono rounded-[8px] bg-[var(--t-accent)] px-3 py-[6px] text-[11.5px] font-extrabold text-[var(--t-accent-ink)] hover:brightness-110 disabled:opacity-50"
              >
                Approve
              </button>
              <button
                type="button"
                disabled={busyId === item.userId}
                onClick={() => { void decide(item.userId, false); }}
                className="hp-mono rounded-[8px] border border-[var(--t-danger-border)] px-3 py-[6px] text-[11.5px] font-extrabold text-[var(--t-danger)] hover:bg-[var(--t-danger-soft)] disabled:opacity-50"
              >
                Decline
              </button>
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}

export default GroupCoachRequestsCard;
