import React, { useState } from 'react';
import { routes } from '../../../utils/routes';
import { useCurrentIdentity, useHubGroupMembership, useMyHubGroups } from '../use-my-hub-groups';
import type { HubGroupDetails, HubGroupLink, HubGroupMember } from '../types';

/**
 * Мелочи страницы групп, нужные И списку `/groups`, И странице `/groups/{slug}`.
 *
 * Выделены при переезде страницы на общий каркас (`deep/entity-page.tsx`, этап C плана
 * docs/plans/entity-page-shell-plan.md): список остался в семье hp, страница уехала в deep,
 * и общий кусок иначе пришлось бы копировать. Логика не менялась — только адрес.
 *
 * Цвета — РОЛИ `--t-*`: они объявлены на `.theme-deep`/`.theme-deep-light`, поэтому одна и
 * та же разметка работает на обеих страницах.
 */

const GROUP_DISCLAIMER =
  'The roster is maintained by the group creator and is not an official club or federation entry.';

// Тот же паттерн antiforgery-токена, что в use-my-hub-groups.ts (там не экспортирован —
// дублируем локально, чтобы не трогать файл, который правит параллельный агент).
let cachedPublicationsToken: string | null = null;

async function fetchPublicationsAntiforgeryToken(): Promise<string | null> {
  if (cachedPublicationsToken) return cachedPublicationsToken;
  try {
    const r = await fetch('/api/antiforgery/token', { credentials: 'include' });
    if (!r.ok) return null;
    const data = await r.json();
    cachedPublicationsToken = data.token ?? null;
    return cachedPublicationsToken;
  } catch {
    return null;
  }
}

async function publicationsApiFetch(url: string, init?: RequestInit): Promise<Response> {
  const method = (init?.method ?? 'GET').toUpperCase();
  if (method === 'GET') return fetch(url, { credentials: 'include', ...init });

  const token = await fetchPublicationsAntiforgeryToken();
  const headers: Record<string, string> = { ...(init?.headers as Record<string, string> | undefined) };
  if (token) headers['X-XSRF-TOKEN'] = token;
  if (init?.body) headers['Content-Type'] = 'application/json';

  const r = await fetch(url, { credentials: 'include', ...init, headers });
  if (!r.ok) cachedPublicationsToken = null;
  return r;
}

const ROLE_LABEL: Record<HubGroupMember['role'], string | null> = {
  member: null,
  captain: 'captain',
  coach: 'coach',
};

const LINK_LABEL: Record<string, string> = {
  whatsapp: 'WhatsApp',
  telegram: 'Telegram',
  instagram: 'Instagram',
  site: 'Site',
};

function groupInitial(name: string): string {
  return (name.trim()[0] ?? '?').toUpperCase();
}

function GroupIcon({ iconUrl, name, size }: { iconUrl?: string | null; name: string; size: 'sm' | 'lg' }) {
  const cls =
    size === 'lg'
      ? 'h-16 w-16 rounded-[18px] text-[26px] lg:h-20 lg:w-20 lg:text-[32px]'
      : 'h-11 w-11 rounded-[13px] text-[18px]';
  if (iconUrl) {
    return <img src={iconUrl} alt="" className={`${cls} shrink-0 object-cover`} />;
  }
  return (
    <span
      className={`${cls} flex shrink-0 items-center justify-center bg-[image:var(--t-accent-grad)] font-black text-[var(--t-accent-ink)]`}
    >
      {groupInitial(name)}
    </span>
  );
}

function LinkChips({ links }: { links: HubGroupLink[] }) {
  if (links.length === 0) return null;
  return (
    <div className="flex flex-wrap gap-2">
      {links.map((l) => (
        <a
          key={l.kind + l.url}
          href={l.url}
          target="_blank"
          rel="noopener noreferrer"
          className="hp-mono rounded-[8px] border border-[var(--t-accent-border)] px-3 py-[5px] text-[12px] font-extrabold text-[var(--t-accent)] no-underline transition-colors hover:border-[var(--t-accent)] hover:bg-[var(--t-accent-soft)]"
        >
          {LINK_LABEL[l.kind] ?? l.kind} ↗
        </a>
      ))}
    </div>
  );
}

function swimmerDisplayName(last: string, first: string, lastEn: string, firstEn: string): string {
  const ru = `${last} ${first}`.trim();
  return ru.length > 0 ? ru : `${lastEn} ${firstEn}`.trim();
}

/** Самозапись в группу (для авторизованного пользователя; не для виртуального «избранного»). */
function JoinButton({ group }: { group: HubGroupDetails }) {
  const { isAuthenticated } = useCurrentIdentity();
  const { joined, join, leave } = useHubGroupMembership();
  const [busy, setBusy] = useState(false);

  if (group.is_virtual || group.id <= 0 || !isAuthenticated) return null;

  const membership = joined.find((j) => j.id === group.id);
  const isPending = membership?.status === 'pending';

  const toggle = async () => {
    setBusy(true);
    try {
      if (membership) await leave(group.id); // active — выход; pending — отмена заявки
      else await join(group.id);
    } finally {
      setBusy(false);
    }
  };

  const label = membership
    ? (isPending ? 'Request sent — cancel' : 'Leave group')
    : (group.join_policy === 'approval' ? 'Request to join' : 'Join group');

  return (
    <button
      type="button"
      disabled={busy}
      onClick={toggle}
      className={
        membership
          ? isPending
            ? 'hp-mono ml-auto shrink-0 rounded-[10px] border border-[var(--t-warn-border)] px-4 py-2 text-[13px] font-extrabold text-[var(--t-warn)] hover:bg-[var(--t-warn-soft)] disabled:opacity-50'
            : 'hp-mono ml-auto shrink-0 rounded-[10px] border border-[var(--t-accent-border)] px-4 py-2 text-[13px] font-extrabold text-[var(--t-accent)] hover:bg-[var(--t-accent-soft)] disabled:opacity-50'
          : 'hp-mono ml-auto shrink-0 rounded-[10px] bg-[var(--t-accent)] px-4 py-2 text-[13px] font-extrabold text-[var(--t-accent-ink)] hover:brightness-110 disabled:opacity-50'
      }
    >
      {label}
    </button>
  );
}

/** Ссылка на тренировки группы — видят участники и управляющие (иначе сервер вернёт 403). */
function TrainingsLink({ group }: { group: HubGroupDetails }) {
  const { isAuthenticated, isAdmin } = useCurrentIdentity();
  const { groups: myGroups } = useMyHubGroups(isAuthenticated);
  const { joined } = useHubGroupMembership();

  if (group.is_virtual || group.id <= 0) return null;
  const manages = isAdmin || myGroups.some((g) => g.id === group.id);
  // pending-заявка доступа не даёт — сервер вернул бы 403, ссылку не показываем.
  const isMember = joined.some((j) => j.id === group.id && j.status === 'active');
  if (!manages && !isMember) return null;

  return (
    <a
      href={`${routes.groupResults(group.slug)}?tab=trainings`}
      className="hp-mono shrink-0 rounded-[10px] border border-[var(--t-accent-border)] bg-[var(--t-accent-soft)] px-4 py-2 text-[13px] font-extrabold text-[var(--t-accent)] no-underline hover:border-[var(--t-accent)]"
      title="Private — visible to the group owner and admins only"
    >
      🔒 Trainings →
    </a>
  );
}


export { GROUP_DISCLAIMER, ROLE_LABEL, LINK_LABEL, groupInitial, GroupIcon, LinkChips, publicationsApiFetch, swimmerDisplayName, JoinButton, TrainingsLink };
