import React, { useState } from 'react';

/**
 * Тумблер политики вступления в группу — карточка таба `Admin` страницы группы.
 *
 * Зачем отдельной карточкой, если такой же селект есть в форме группы на `/groups`: форма
 * живёт в панели «My groups» и открывается ради полного редактирования (имя, slug, ссылки),
 * а этот выбор — про доступ к members-контенту, и решают его на самой странице группы, где
 * рядом стоит инбокс заявок. Управление сущностью живёт в одном месте (план §3.8).
 *
 * Почему узкая ручка `PUT /api/me/hub-groups/{id}/join-policy`, а не общий Update: серверный
 * `HubGroupCrudCore.Apply` перезаписывает ВСЕ поля из DTO, и «поменять один тумблер» через
 * него значило бы слать форму целиком — карточка её не знает и затёрла бы имя со ссылками.
 *
 * Переключение НЕ трогает уже вступивших: `approval` — дверь для новых, а не ретроактивный
 * пересмотр состава (кого пустили при `open`, того убирают руками в списке участников).
 */

/** Токен antiforgery: свой кэш на модуль — как у остальных мутирующих клиентов проекта. */
let cachedToken: string | null = null;

async function apiPut(url: string, body: unknown): Promise<boolean> {
  if (!cachedToken) {
    try {
      const r = await fetch('/api/antiforgery/token', { credentials: 'include' });
      cachedToken = r.ok ? (await r.json()).token ?? null : null;
    } catch {
      cachedToken = null;
    }
  }
  if (!cachedToken) return false;
  try {
    const r = await fetch(url, {
      method: 'PUT',
      credentials: 'include',
      headers: { 'Content-Type': 'application/json', 'X-XSRF-TOKEN': cachedToken },
      body: JSON.stringify(body),
    });
    if (!r.ok) cachedToken = null;
    return r.ok;
  } catch {
    cachedToken = null;
    return false;
  }
}

type Policy = 'open' | 'approval';

const OPTIONS: { value: Policy; title: string; hint: string }[] = [
  {
    value: 'open',
    title: 'Open',
    hint: 'Anyone can join instantly — and immediately sees members-only media.',
  },
  {
    value: 'approval',
    title: 'By request',
    hint: 'Joining creates a request. Members-only media stays hidden until you approve it.',
  },
];

function GroupJoinPolicyCard({ groupId, policy }: { groupId: number; policy: Policy }) {
  const [value, setValue] = useState<Policy>(policy);
  const [saving, setSaving] = useState<Policy | null>(null);
  const [error, setError] = useState<string | null>(null);

  const pick = async (next: Policy) => {
    if (next === value || saving != null) return;
    setSaving(next);
    setError(null);
    const ok = await apiPut(`/api/me/hub-groups/${groupId}/join-policy`, { joinPolicy: next });
    setSaving(null);
    if (ok) setValue(next);
    else setError('Could not save. Try again.');
  };

  return (
    <div className="deep-card">
      <div className="deep-card-title">Joining</div>
      <div className="deep-card-sub mt-1">who gets in, and who sees members-only media</div>

      <div className="mt-3 flex flex-col gap-2">
        {OPTIONS.map((o) => {
          const on = value === o.value;
          return (
            <button
              key={o.value}
              type="button"
              onClick={() => pick(o.value)}
              aria-pressed={on}
              disabled={saving != null}
              className={`cursor-pointer rounded-[12px] border p-[10px_12px] text-left transition-colors disabled:opacity-60 ${
                on
                  ? 'border-[var(--t-accent)] bg-[var(--t-accent-soft)]'
                  : 'border-[var(--t-border)] bg-transparent hover:bg-[var(--t-surface2)]'
              }`}
            >
              <span className="flex items-center gap-2">
                <span
                  className={`inline-block h-[9px] w-[9px] shrink-0 rounded-full ${
                    on ? 'bg-[var(--t-accent)]' : 'bg-[var(--t-border)]'
                  }`}
                />
                <span className={`text-[13px] font-black ${on ? 'text-[var(--t-accent)]' : 'text-[var(--t-text)]'}`}>
                  {o.title}
                </span>
                {saving === o.value && <span className="text-[11px] text-[var(--t-text-3)]">saving…</span>}
              </span>
              <span className="mt-1 block text-[11.5px] leading-[1.45] text-[var(--t-text-2)]">{o.hint}</span>
            </button>
          );
        })}
      </div>

      {value === 'open' && (
        <p className="m-0 mt-2.5 text-[11.5px] italic text-[var(--t-warn)]">
          Anyone who joins sees members-only media — including videos of children. Use “By request”
          if that is not what you want.
        </p>
      )}
      {error && <p className="m-0 mt-2 text-[12px] font-bold text-[var(--t-danger)]">{error}</p>}
      <p className="m-0 mt-2 text-[11px] text-[var(--t-text-3)]">
        Changing this does not affect people who already joined.
      </p>
    </div>
  );
}

export default GroupJoinPolicyCard;
