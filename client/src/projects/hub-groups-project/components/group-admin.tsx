import React, { useEffect, useState } from 'react';
import { useCurrentIdentity, useMyHubGroups } from '../use-my-hub-groups';
import { publicationsApiFetch, SwimContextLine } from './group-bits';
import type { GroupPublicationItem, HubGroupDetails } from '../types';

/**
 * Инбокс заявок на публикацию — содержимое таба `Admin` страницы группы.
 *
 * Правило (план §3.8): управление сущностью живёт в табе `Admin` и видно только
 * управляющим — `manages = isAdmin || это моя группа`. Тому, кто группой не управляет,
 * таб не показывается вовсе, поэтому внутренний ранний выход здесь остаётся страховкой.
 */

const PUBLICATION_LEVEL_LABEL: Record<GroupPublicationItem['level'], string> = {
  public: 'public',
  members: 'members',
};

/**
 * Карточка «Заявки на публикацию» — inbox модерации для управляющих (CanEdit).
 * Признак «я управляю группой» — тот же источник, что у TrainingsLink (isAdmin || moй список групп).
 * После решения — рефетч inbox и обоих published-списков (bump reloadKey наверх).
 */
function PublicationsInbox({ group, onDecided }: { group: HubGroupDetails; onDecided: () => void }) {
  const { isAuthenticated, isAdmin } = useCurrentIdentity();
  const { groups: myGroups } = useMyHubGroups(isAuthenticated);
  const manages = isAdmin || myGroups.some((g) => g.id === group.id);

  const [items, setItems] = useState<GroupPublicationItem[]>([]);
  const [busyId, setBusyId] = useState<number | null>(null);

  const reload = React.useCallback(async () => {
    if (!manages || group.is_virtual || group.id <= 0) { setItems([]); return; }
    try {
      const r = await fetch(`/api/hub-groups/${group.id}/media/publications`, { credentials: 'include' });
      setItems(r.ok ? await r.json() : []);
    } catch {
      setItems([]);
    }
  }, [manages, group.id, group.is_virtual]);

  useEffect(() => { reload(); }, [reload]);

  if (!manages || items.length === 0) return null;

  const decide = async (publicationId: number, approve: boolean) => {
    setBusyId(publicationId);
    try {
      const r = await publicationsApiFetch(`/api/hub-groups/${group.id}/media/publications/${publicationId}/decision`, {
        method: 'POST',
        body: JSON.stringify({ approve }),
      });
      if (r.ok) {
        await reload();
        onDecided();
      }
    } finally {
      setBusyId(null);
    }
  };

  const badgeCls = 'hp-mono rounded-[6px] border border-[var(--t-accent-border)] px-[6px] py-[2px] text-[10px] font-extrabold text-[var(--t-accent)]';
  const statusCls: Record<GroupPublicationItem['status'], string> = {
    pending: 'text-[var(--t-warn)]',
    approved: 'text-[var(--t-accent)]',
    rejected: 'text-[var(--t-danger)]',
  };

  return (
    <div id="publications-inbox" className="deep-card" aria-label="Publications inbox">
      <h2 className="deep-card-title mb-4">Publication requests</h2>
      <div className="flex flex-col gap-2">
        {items.map((item) => {
          let domain = item.url;
          try { domain = new URL(item.url).hostname; } catch { /* оставляем как есть */ }
          return (
            <div key={item.id} className="flex flex-wrap items-center gap-3 rounded-[10px] border border-[var(--t-border)] p-2">
              <a
                href={item.url}
                target="_blank"
                rel="noopener noreferrer nofollow"
                className="hp-mono shrink-0 text-[12px] font-extrabold text-[var(--t-accent)] no-underline hover:underline"
              >
                {domain} ↗
              </a>
              <div className="min-w-0 flex-1">
                {item.swimmer_name && (
                  <p className="m-0 truncate text-[13px] font-extrabold text-[var(--t-text)]">{item.swimmer_name}</p>
                )}
                {item.result_label && (
                  <SwimContextLine
                    label={item.result_label}
                    competitionId={item.competition_id}
                    resultId={item.result_id != null ? Number(item.result_id) : null}
                    swimmerId={item.swimmer_id}
                    className="text-[11.5px] text-[var(--t-text-2)]"
                  />
                )}
                <p className="m-0 truncate text-[11px] text-[var(--t-text-3)]">{item.owner_email}</p>
              </div>
              <span className={badgeCls}>{PUBLICATION_LEVEL_LABEL[item.level]}</span>
              <span className={`hp-mono text-[10.5px] font-extrabold uppercase ${statusCls[item.status]}`}>
                {item.status}
              </span>
              <div className="flex shrink-0 gap-2">
                {item.status === 'pending' && (
                  <>
                    <button
                      type="button"
                      disabled={busyId === item.id}
                      onClick={() => decide(item.id, true)}
                      className="hp-mono rounded-[8px] bg-[var(--t-accent)] px-3 py-[6px] text-[11.5px] font-extrabold text-[var(--t-accent-ink)] hover:brightness-110 disabled:opacity-50"
                    >
                      Publish
                    </button>
                    <button
                      type="button"
                      disabled={busyId === item.id}
                      onClick={() => decide(item.id, false)}
                      className="hp-mono rounded-[8px] border border-[var(--t-danger-border)] px-3 py-[6px] text-[11.5px] font-extrabold text-[var(--t-danger)] hover:bg-[var(--t-danger-soft)] disabled:opacity-50"
                    >
                      Decline
                    </button>
                  </>
                )}
                {item.status === 'approved' && (
                  <button
                    type="button"
                    disabled={busyId === item.id}
                    onClick={() => decide(item.id, false)}
                    className="hp-mono rounded-[8px] border border-[var(--t-warn-border)] px-3 py-[6px] text-[11.5px] font-extrabold text-[var(--t-warn)] hover:bg-[var(--t-warn-soft)] disabled:opacity-50"
                  >
                    Unpublish
                  </button>
                )}
              </div>
            </div>
          );
        })}
      </div>
    </div>
  );
}


export default PublicationsInbox;
