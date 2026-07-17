import React, { useEffect, useMemo, useState } from 'react';
import '../home-project/home.css';
import AppTopbar from '../components/app-topbar/app-topbar';
import RecordTicker from '../home-project/components/record-ticker';
import MyGroupsPanel from './my-groups-panel';
import UI_ClubIcon from '../components/mix/club-icon/club-icon';
import UI_FlagEmoji from '../components/mix/flag-icon/flag-icon';
import UI_SwimmerGallery from '../components/mix/swimmer-gallery/swimmer-gallery';
import { GalleryItem } from '../../utils/interfaces/results';
import { HelperMedia } from '../../utils/helpers';
import { useCurrentIdentity, useHubGroupMembership, useMyHubGroups } from './use-my-hub-groups';
import type {
  GroupPublicationItem, HubGroupDetails, HubGroupLink, HubGroupListItem, HubGroupMediaItem,
  HubGroupMember, HubGroupMemberMediaItem, HubGroupStanding,
} from './types';

const GROUP_DISCLAIMER =
  'Состав ведётся создателем группы и не является официальной заявкой клуба или федерации.';

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

// Страница групп (HubGroups, фазы 3–4): список публичных групп + страница группы
// с участниками, «рекордами группы» и последними заплывами. Виртуальная группа
// «Моё избранное» (/api/hub-groups/favorites) показывается первой залогиненному.

const ROLE_LABEL: Record<HubGroupMember['role'], string | null> = {
  member: null,
  captain: 'капитан',
  coach: 'тренер',
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
      className={`${cls} flex shrink-0 items-center justify-center bg-[linear-gradient(140deg,#38bdf8,#0369a1)] font-black text-[#06263f]`}
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
          className="hp-mono rounded-[8px] border border-[#7dd3fc]/40 px-3 py-[5px] text-[12px] font-extrabold text-[#7dd3fc] no-underline transition-colors hover:border-[#7dd3fc] hover:bg-[rgba(56,189,248,0.12)]"
        >
          {LINK_LABEL[l.kind] ?? l.kind} ↗
        </a>
      ))}
    </div>
  );
}

// ── Список групп ─────────────────────────────────────────────────────────────

function GroupCard({ group, href }: { group: HubGroupListItem; href: string }) {
  return (
    <a
      href={href}
      className="hp-card-std flex min-h-[130px] flex-col justify-between gap-4 rounded-[18px] border border-[#7dd3fc]/[0.22] p-[18px] text-inherit no-underline shadow-[0_24px_60px_rgba(2,10,24,0.5)] backdrop-blur-[14px] transition-[transform,border-color,box-shadow] duration-[180ms] ease-out hover:-translate-y-2 hover:border-[#7dd3fc]/80 lg:rounded-[24px] lg:p-[26px]"
    >
      <div className="flex items-start gap-4">
        <GroupIcon iconUrl={group.icon_url} name={group.name_en || group.name} size="sm" />
        <div className="min-w-0">
          <div className="flex items-center gap-2">
            <div className="truncate text-[19px] font-black tracking-[-0.02em] lg:text-[22px]">
              {group.name}
            </div>
            {group.is_official && group.club_name && (
              <UI_ClubIcon clubName={group.club_name} iconWidth="6" styleType="icon-notext" />
            )}
          </div>
          {group.name_en && group.name_en !== group.name && (
            <div className="truncate text-[12px] font-bold text-[#cbe0f0]/60">{group.name_en}</div>
          )}
        </div>
      </div>
      {group.description && (
        <p className="line-clamp-2 text-[13px] leading-snug text-[#cbe0f0]/75">{group.description}</p>
      )}
      <div className="flex items-center justify-between">
        <span className="hp-mono rounded-[7px] border border-[#7dd3fc]/40 px-2 py-[3px] text-[11px] font-extrabold text-[#7dd3fc]">
          {group.member_count} · swimmers
        </span>
        <span className="inline-flex min-w-0 items-center gap-1.5 truncate pl-3 text-[12px] font-bold text-[#cbe0f0]/60">
          {group.country && <UI_FlagEmoji countryCode={group.country} size="16x12" />}
          {group.location ?? group.club_name ?? ''}
        </span>
      </div>
    </a>
  );
}

function GroupsList({ groups, favorites }: { groups: HubGroupListItem[]; favorites: HubGroupDetails | null }) {
  return (
    <>
      <section className="relative px-5 pt-[26px] lg:px-16 lg:pt-[46px]">
        <p className="mb-[18px] text-[11px] font-extrabold uppercase tracking-[0.28em] text-[#7dd3fc] lg:text-[15px] lg:tracking-[0.3em]">
          Train together · Follow together
        </p>
        <h1 className="text-[44px] font-black leading-[0.92] tracking-[-0.045em] text-[#f3f8fd] lg:text-[88px] lg:leading-[0.9]">
          Groups
        </h1>
        <p className="mt-5 max-w-[560px] text-[14.5px] leading-[1.55] text-[#e2f0fc]/[0.82] lg:text-[18px] lg:leading-[1.6]">
          Тренировочные группы: состав, рекорды группы и свежие заплывы участников.
        </p>
      </section>

      <section
        className="grid grid-cols-1 gap-3 px-4 pt-[26px] sm:grid-cols-2 lg:grid-cols-3 lg:gap-[18px] lg:px-16 lg:pt-12"
        aria-label="Groups"
      >
        {favorites && (
          <GroupCard
            href="./groups.html?group=favorites"
            group={{
              slug: 'favorites',
              name: favorites.name,
              name_en: favorites.name_en,
              description: 'Пловцы из твоего избранного — как личная группа',
              icon_url: null,
              location: null,
              club_name: null,
              is_official: false,
              member_count: favorites.members.length,
            }}
          />
        )}
        {groups.map((g) => (
          <GroupCard key={g.slug} group={g} href={`./groups.html?group=${encodeURIComponent(g.slug)}`} />
        ))}
        {groups.length === 0 && !favorites && (
          <p className="col-span-full py-10 text-center text-[14px] text-[#cbe0f0]/60">
            Групп пока нет.
          </p>
        )}
      </section>
    </>
  );
}

// ── Страница группы ──────────────────────────────────────────────────────────

const BESTS_PREVIEW_COUNT = 10;

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
    ? (isPending ? 'Заявка отправлена — отменить' : 'Выйти из группы')
    : (group.join_policy === 'approval' ? 'Подать заявку' : 'Вступить в группу');

  return (
    <button
      type="button"
      disabled={busy}
      onClick={toggle}
      className={
        membership
          ? isPending
            ? 'hp-mono ml-auto shrink-0 rounded-[10px] border border-[#ffca7a]/50 px-4 py-2 text-[13px] font-extrabold text-[#ffca7a] hover:bg-[#ffca7a]/10 disabled:opacity-50'
            : 'hp-mono ml-auto shrink-0 rounded-[10px] border border-[#7dd3fc]/40 px-4 py-2 text-[13px] font-extrabold text-[#7dd3fc] hover:bg-[#7dd3fc]/10 disabled:opacity-50'
          : 'hp-mono ml-auto shrink-0 rounded-[10px] bg-[#38ef8f] px-4 py-2 text-[13px] font-extrabold text-[#04101f] hover:brightness-110 disabled:opacity-50'
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
      href={`./results_main.html?group=${encodeURIComponent(group.slug)}&tab=trainings`}
      className="hp-mono shrink-0 rounded-[10px] border border-[#38bdf8]/50 bg-[rgba(56,189,248,0.1)] px-4 py-2 text-[13px] font-extrabold text-[#7dd3fc] no-underline hover:bg-[rgba(56,189,248,0.18)]"
      title="Private — visible to the group owner and admins only"
    >
      🔒 Trainings →
    </a>
  );
}

// ── Галерея группы ───────────────────────────────────────────────────────────

function GalleryTile({ item, onClick }: { item: HubGroupMediaItem; onClick?: () => void }) {
  const captionOverlay = item.caption && (
    <div className="absolute inset-x-0 bottom-0 truncate bg-[linear-gradient(0deg,rgba(2,10,24,0.85),transparent)] px-2 py-1 text-[11px] font-bold text-[#f3f8fd]">
      {item.caption}
    </div>
  );

  if (item.media_type === 'album') {
    return (
      <a
        href={item.url}
        target="_blank"
        rel="noopener noreferrer"
        className="hp-mono flex aspect-square flex-col items-center justify-center gap-1 rounded-[12px] border border-[#7dd3fc]/30 bg-[rgba(6,20,36,0.6)] p-2 text-center text-[12px] font-extrabold text-[#7dd3fc] no-underline hover:border-[#7dd3fc]/70 hover:bg-[rgba(56,189,248,0.12)]"
      >
        <span className="text-[24px]">📂</span>
        <span className="line-clamp-2">{item.caption || 'Album'} ↗</span>
      </a>
    );
  }

  const thumbUrl = HelperMedia.resolveThumbUrl(item.media_type, item.source_type, item.url);

  return (
    <div
      className="relative aspect-square cursor-pointer overflow-hidden rounded-[12px] border border-[#7dd3fc]/20 bg-[rgba(6,20,36,0.6)]"
      onClick={onClick}
    >
      {thumbUrl ? (
        <img loading="lazy" src={thumbUrl} alt="" className="h-full w-full object-cover" />
      ) : (
        <div className="flex h-full w-full items-center justify-center text-[28px]">🎬</div>
      )}
      {item.media_type === 'video' && (
        <span className="absolute right-1.5 top-1.5 rounded-full bg-[rgba(2,10,24,0.7)] px-1.5 py-1 text-[13px] leading-none">
          ▶
        </span>
      )}
      {captionOverlay}
    </div>
  );
}

function GroupGallery({ gallery }: { gallery: HubGroupMediaItem[] }) {
  const [openIndex, setOpenIndex] = useState<number | null>(null);

  // Лайтбокс открывается только для image/video — album ссылка внешняя (target=_blank).
  // Один экземпляр лайтбокса на сетку; массив и карта индексов мемоизированы.
  const { lightboxGalleryItems, indexById } = useMemo(() => {
    const lightboxItems = gallery.filter((g) => g.media_type !== 'album');
    return {
      lightboxGalleryItems: lightboxItems.map((g): GalleryItem => ({
        type: g.media_type === 'video' ? 'video' : 'image',
        sourceType: g.source_type === 'album' ? undefined : (g.source_type as GalleryItem['sourceType']),
        url: g.url,
      })),
      indexById: new Map(lightboxItems.map((g, i) => [g.id, i])),
    };
  }, [gallery]);

  if (gallery.length === 0) return null;

  return (
    <div id="gallery" className="hp-card-std rounded-[18px] border border-[#7dd3fc]/[0.22] p-[18px] shadow-[0_24px_60px_rgba(2,10,24,0.5)] backdrop-blur-[14px] lg:rounded-[24px] lg:p-[26px]" aria-label="Gallery">
      <h2 className="mb-4 text-[15px] font-black uppercase tracking-[0.2em] text-[#7dd3fc]">Gallery</h2>
      <div className="grid grid-cols-3 gap-2 sm:grid-cols-4 lg:grid-cols-5">
        {gallery.map((item) => (
          <GalleryTile
            key={item.id}
            item={item}
            onClick={item.media_type === 'album' ? undefined : () => setOpenIndex(indexById.get(item.id) ?? 0)}
          />
        ))}
      </div>
      <UI_SwimmerGallery
        gallery={lightboxGalleryItems}
        openIndex={openIndex}
        onClose={() => setOpenIndex(null)}
      />
    </div>
  );
}

/**
 * 🔒 Members reviews (2B′): тренерские разборы — members-медиа группы с якорем пловец/заплыв.
 * Доступ решает сервер (403/401 → секция молча скрыта), клиент без логина даже не грузит.
 * Рендер видео — тот же лайтбокс, что у галереи (embed из канонического id, не сырого URL).
 */
function MembersReviews({ group }: { group: HubGroupDetails }) {
  const { isAuthenticated } = useCurrentIdentity();
  const [items, setItems] = useState<HubGroupMemberMediaItem[]>([]);
  const [openIndex, setOpenIndex] = useState<number | null>(null);

  useEffect(() => {
    if (!isAuthenticated || group.is_virtual || group.id <= 0) { setItems([]); return; }
    let alive = true;
    fetch(`/api/hub-groups/${encodeURIComponent(group.slug)}/media/members`, { credentials: 'include' })
      .then((r) => (r.ok ? (r.json() as Promise<HubGroupMemberMediaItem[]>) : []))
      .then((data) => { if (alive) setItems(data); })
      .catch(() => { if (alive) setItems([]); });
    return () => { alive = false; };
  }, [isAuthenticated, group.slug, group.is_virtual, group.id]);

  const { lightboxGalleryItems, indexById } = useMemo(() => {
    const lightboxItems = items.filter((g) => g.media_type !== 'album');
    return {
      lightboxGalleryItems: lightboxItems.map((g): GalleryItem => ({
        type: g.media_type === 'video' ? 'video' : 'image',
        sourceType: g.source_type === 'album' ? undefined : (g.source_type as GalleryItem['sourceType']),
        url: g.url,
      })),
      indexById: new Map(lightboxItems.map((g, i) => [g.id, i])),
    };
  }, [items]);

  if (items.length === 0) return null;

  return (
    <div id="members-reviews" className="hp-card-std rounded-[18px] border border-[#7dd3fc]/[0.22] p-[18px] shadow-[0_24px_60px_rgba(2,10,24,0.5)] backdrop-blur-[14px] lg:rounded-[24px] lg:p-[26px]" aria-label="Members reviews">
      <h2 className="mb-1 text-[15px] font-black uppercase tracking-[0.2em] text-[#7dd3fc]">🔒 Reviews</h2>
      <p className="mb-4 text-[11.5px] italic text-[#cbe0f0]/45">Visible to group members only.</p>
      <div className="flex flex-col gap-2">
        {items.map((item) => (
          <div key={item.id} className="flex items-center gap-3">
            <div className="w-[96px] shrink-0">
              <GalleryTile
                item={{ id: item.id, media_type: item.media_type, source_type: item.source_type, url: item.url, caption: null }}
                onClick={item.media_type === 'album' ? undefined : () => setOpenIndex(indexById.get(item.id) ?? 0)}
              />
            </div>
            <div className="min-w-0">
              {item.swimmer_name && (
                <p className="m-0 truncate text-[13.5px] font-extrabold text-[#f3f8fd]">{item.swimmer_name}</p>
              )}
              {item.result_label && (
                <p className="m-0 truncate text-[12px] text-[#7dd3fc]/80">{item.result_label}</p>
              )}
              {item.caption && (
                <p className="m-0 truncate text-[12px] text-[#cbe0f0]/70">{item.caption}</p>
              )}
            </div>
          </div>
        ))}
      </div>
      <UI_SwimmerGallery
        gallery={lightboxGalleryItems}
        openIndex={openIndex}
        onClose={() => setOpenIndex(null)}
      />
    </div>
  );
}

/** Подпись тайла публикации — swimmer_name (+ result_label, если есть). */
function publicationCaption(item: GroupPublicationItem): string | null {
  const parts = [item.swimmer_name, item.result_label].filter(Boolean) as string[];
  return parts.length > 0 ? parts.join(' · ') : null;
}

function publicationsLightbox(items: GroupPublicationItem[]) {
  const lightboxItems = items.filter((g) => g.media_type !== 'album');
  return {
    lightboxGalleryItems: lightboxItems.map((g): GalleryItem => ({
      type: g.media_type === 'video' ? 'video' : 'image',
      sourceType: g.source_type === 'album' ? undefined : (g.source_type as GalleryItem['sourceType']),
      url: g.url,
    })),
    indexById: new Map(lightboxItems.map((g, i) => [g.id, i])),
  };
}

/**
 * «From members» (public-слой публикаций): одобренные public-публикации участников,
 * доступно всем (включая анонимов) — под Gallery. Пустой список → секция не рендерится.
 */
function FromMembersGallery({ group }: { group: HubGroupDetails }) {
  const [items, setItems] = useState<GroupPublicationItem[]>([]);
  const [openIndex, setOpenIndex] = useState<number | null>(null);

  useEffect(() => {
    if (group.is_virtual || !group.slug) { setItems([]); return; }
    let alive = true;
    fetch(`/api/hub-groups/${encodeURIComponent(group.slug)}/media/published?level=public`)
      .then((r) => (r.ok ? (r.json() as Promise<GroupPublicationItem[]>) : []))
      .then((data) => { if (alive) setItems(data); })
      .catch(() => { if (alive) setItems([]); });
    return () => { alive = false; };
  }, [group.slug, group.is_virtual]);

  const { lightboxGalleryItems, indexById } = useMemo(() => publicationsLightbox(items), [items]);

  if (items.length === 0) return null;

  return (
    <div id="from-members" className="hp-card-std rounded-[18px] border border-[#7dd3fc]/[0.22] p-[18px] shadow-[0_24px_60px_rgba(2,10,24,0.5)] backdrop-blur-[14px] lg:rounded-[24px] lg:p-[26px]" aria-label="From members">
      <h2 className="mb-4 text-[15px] font-black uppercase tracking-[0.2em] text-[#7dd3fc]">From members</h2>
      <div className="grid grid-cols-3 gap-2 sm:grid-cols-4 lg:grid-cols-5">
        {items.map((item) => (
          <GalleryTile
            key={item.id}
            item={{
              id: item.id, media_type: item.media_type, source_type: item.source_type,
              url: item.url, caption: publicationCaption(item),
            }}
            onClick={item.media_type === 'album' ? undefined : () => setOpenIndex(indexById.get(item.id) ?? 0)}
          />
        ))}
      </div>
      <UI_SwimmerGallery
        gallery={lightboxGalleryItems}
        openIndex={openIndex}
        onClose={() => setOpenIndex(null)}
      />
    </div>
  );
}

/**
 * Публикации участников members-слоя — рендерится ВНУТРИ members-секции, после тренерских
 * разборов; fetch делается только когда members-секция вообще доступна (та же логика,
 * что у MembersReviews — авторизован + группа реальная).
 */
function MembersPublications({ group }: { group: HubGroupDetails }) {
  const { isAuthenticated } = useCurrentIdentity();
  const [items, setItems] = useState<GroupPublicationItem[]>([]);
  const [openIndex, setOpenIndex] = useState<number | null>(null);

  useEffect(() => {
    if (!isAuthenticated || group.is_virtual || group.id <= 0) { setItems([]); return; }
    let alive = true;
    fetch(`/api/hub-groups/${encodeURIComponent(group.slug)}/media/published?level=members`, { credentials: 'include' })
      .then((r) => (r.ok ? (r.json() as Promise<GroupPublicationItem[]>) : []))
      .then((data) => { if (alive) setItems(data); })
      .catch(() => { if (alive) setItems([]); });
    return () => { alive = false; };
  }, [isAuthenticated, group.slug, group.is_virtual, group.id]);

  const { lightboxGalleryItems, indexById } = useMemo(() => publicationsLightbox(items), [items]);

  if (items.length === 0) return null;

  return (
    <div id="members-publications" className="hp-card-std rounded-[18px] border border-[#7dd3fc]/[0.22] p-[18px] shadow-[0_24px_60px_rgba(2,10,24,0.5)] backdrop-blur-[14px] lg:rounded-[24px] lg:p-[26px]" aria-label="Members publications">
      <h2 className="mb-4 text-[15px] font-black uppercase tracking-[0.2em] text-[#7dd3fc]">From members</h2>
      <div className="grid grid-cols-3 gap-2 sm:grid-cols-4 lg:grid-cols-5">
        {items.map((item) => (
          <GalleryTile
            key={item.id}
            item={{
              id: item.id, media_type: item.media_type, source_type: item.source_type,
              url: item.url, caption: publicationCaption(item),
            }}
            onClick={item.media_type === 'album' ? undefined : () => setOpenIndex(indexById.get(item.id) ?? 0)}
          />
        ))}
      </div>
      <UI_SwimmerGallery
        gallery={lightboxGalleryItems}
        openIndex={openIndex}
        onClose={() => setOpenIndex(null)}
      />
    </div>
  );
}

const PUBLICATION_LEVEL_LABEL: Record<GroupPublicationItem['level'], string> = {
  public: 'публично',
  members: 'участникам',
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

  const badgeCls = 'hp-mono rounded-[6px] border border-[#7dd3fc]/40 px-[6px] py-[2px] text-[10px] font-extrabold text-[#7dd3fc]';
  const statusCls: Record<GroupPublicationItem['status'], string> = {
    pending: 'text-[#ffca7a]',
    approved: 'text-[#38ef8f]',
    rejected: 'text-[#ef5350]',
  };

  return (
    <div id="publications-inbox" className="hp-card-std rounded-[18px] border border-[#7dd3fc]/[0.22] p-[18px] shadow-[0_24px_60px_rgba(2,10,24,0.5)] backdrop-blur-[14px] lg:rounded-[24px] lg:p-[26px]" aria-label="Publications inbox">
      <h2 className="mb-4 text-[15px] font-black uppercase tracking-[0.2em] text-[#7dd3fc]">Заявки на публикацию</h2>
      <div className="flex flex-col gap-2">
        {items.map((item) => {
          let domain = item.url;
          try { domain = new URL(item.url).hostname; } catch { /* оставляем как есть */ }
          return (
            <div key={item.id} className="flex flex-wrap items-center gap-3 rounded-[10px] border border-[#7dd3fc]/15 p-2">
              <a
                href={item.url}
                target="_blank"
                rel="noopener noreferrer nofollow"
                className="hp-mono shrink-0 text-[12px] font-extrabold text-[#7dd3fc] no-underline hover:underline"
              >
                {domain} ↗
              </a>
              <div className="min-w-0 flex-1">
                {item.swimmer_name && (
                  <p className="m-0 truncate text-[13px] font-extrabold text-[#f3f8fd]">{item.swimmer_name}</p>
                )}
                {item.result_label && (
                  <p className="m-0 truncate text-[11.5px] text-[#cbe0f0]/70">{item.result_label}</p>
                )}
                <p className="m-0 truncate text-[11px] text-[#cbe0f0]/50">{item.owner_email}</p>
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
                      className="hp-mono rounded-[8px] bg-[#38ef8f] px-3 py-[6px] text-[11.5px] font-extrabold text-[#04101f] hover:brightness-110 disabled:opacity-50"
                    >
                      Опубликовать
                    </button>
                    <button
                      type="button"
                      disabled={busyId === item.id}
                      onClick={() => decide(item.id, false)}
                      className="hp-mono rounded-[8px] border border-[#ef5350]/50 px-3 py-[6px] text-[11.5px] font-extrabold text-[#ef5350] hover:bg-[#ef5350]/10 disabled:opacity-50"
                    >
                      Отклонить
                    </button>
                  </>
                )}
                {item.status === 'approved' && (
                  <button
                    type="button"
                    disabled={busyId === item.id}
                    onClick={() => decide(item.id, false)}
                    className="hp-mono rounded-[8px] border border-[#ffca7a]/50 px-3 py-[6px] text-[11.5px] font-extrabold text-[#ffca7a] hover:bg-[#ffca7a]/10 disabled:opacity-50"
                  >
                    Снять
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

function GroupDetails({ group }: { group: HubGroupDetails }) {
  const [showAllBests, setShowAllBests] = useState(false);
  // Бампится после решения в inbox — форсирует рефетч обоих published-списков (key-ремаунт).
  const [publicationsReloadKey, setPublicationsReloadKey] = useState(0);
  const bests = showAllBests ? group.bests : group.bests.slice(0, BESTS_PREVIEW_COUNT);

  const cellCls = 'px-3 py-2 text-left text-[13px] text-[#e2f0fc]/[0.85]';
  const headCls =
    'px-3 py-2 text-left text-[10.5px] font-extrabold uppercase tracking-[0.18em] text-[#7dd3fc]';
  const cardCls =
    'hp-card-std rounded-[18px] border border-[#7dd3fc]/[0.22] p-[18px] shadow-[0_24px_60px_rgba(2,10,24,0.5)] backdrop-blur-[14px] lg:rounded-[24px] lg:p-[26px]';

  return (
    <>
      <section className="relative px-5 pt-[26px] lg:px-16 lg:pt-[46px]">
        <a href="./groups.html" className="text-[13px] font-extrabold text-[#7dd3fc] no-underline hover:underline">
          ← All groups
        </a>

        <div className="mt-5 flex flex-wrap items-center gap-5">
          <GroupIcon iconUrl={group.icon_url} name={group.name_en || group.name} size="lg" />
          <div className="min-w-0">
            <h1 className="text-[34px] font-black leading-[0.95] tracking-[-0.04em] text-[#f3f8fd] lg:text-[56px]">
              {group.name}
            </h1>
            {group.name_en && group.name_en !== group.name && (
              <p className="mt-1 text-[14px] font-bold text-[#cbe0f0]/60">{group.name_en}</p>
            )}
            {group.is_official && group.club_name && (
              <div className="mt-2 flex items-center gap-2">
                <UI_ClubIcon clubName={group.club_name} iconWidth="6" styleType="icon-notext" />
                <span className="hp-mono rounded-[8px] border border-[#38ef8f]/40 bg-[rgba(56,239,143,0.1)] px-3 py-[5px] text-[12px] font-extrabold text-[#38ef8f]">
                  Official Group of {group.club_name}
                </span>
              </div>
            )}
          </div>
          <div className="ml-auto flex shrink-0 items-center gap-2">
            {!group.is_virtual && group.id > 0 && (
              <a
                href={`./results_main.html?group=${encodeURIComponent(group.slug)}`}
                className="hp-mono shrink-0 rounded-[10px] border border-[#7dd3fc]/40 bg-[rgba(125,211,252,0.08)] px-4 py-2 text-[13px] font-extrabold text-[#7dd3fc] no-underline hover:bg-[rgba(125,211,252,0.16)]"
              >
                Competitions →
              </a>
            )}
            <TrainingsLink group={group} />
            <JoinButton group={group} />
          </div>
        </div>

        {group.description && (
          <p className="mt-4 max-w-[640px] text-[14.5px] leading-[1.55] text-[#e2f0fc]/[0.82] lg:text-[16px]">
            {group.description}
          </p>
        )}

        <div className="mt-4 flex flex-wrap items-center gap-x-5 gap-y-2 text-[13px] font-bold text-[#cbe0f0]/70">
          {(group.country || group.location) && (
            <span className="inline-flex items-center gap-1.5">
              {group.country
                ? <UI_FlagEmoji countryCode={group.country} size="16x12" />
                : '📍'}
              {group.location}
            </span>
          )}
          {group.club_name && <span>Клуб: {group.club_name}</span>}
          <span>
            {group.members.length} · swimmers
          </span>
        </div>

        <div className="mt-4">
          <LinkChips links={group.links} />
        </div>
      </section>

      <section className="grid grid-cols-1 gap-4 px-4 pt-[26px] lg:grid-cols-[320px_1fr] lg:gap-[18px] lg:px-16 lg:pt-10">
        {/* Участники */}
        <div id="members" className={cardCls} aria-label="Members">
          <h2 className="mb-4 text-[15px] font-black uppercase tracking-[0.2em] text-[#7dd3fc]">Members</h2>
          {group.members.length === 0 ? (
            <p className="text-[13px] text-[#cbe0f0]/60">
              {group.is_virtual ? 'В избранном пока нет пловцов — жми сердечки на результатах.' : 'Состав пока не заполнен.'}
            </p>
          ) : (
            <ul className="m-0 flex list-none flex-col gap-[10px] p-0">
              {group.members.map((m) => (
                <li key={m.swimmer_id} className="flex items-center justify-between gap-3">
                  <div className="min-w-0">
                    <div className="truncate text-[14px] font-extrabold text-[#f3f8fd]">
                      {m.name || m.name_en}
                    </div>
                    <div className="truncate text-[11.5px] text-[#cbe0f0]/55">
                      {[m.birth_year > 0 ? m.birth_year : null, m.club_name].filter(Boolean).join(' · ')}
                    </div>
                  </div>
                  {ROLE_LABEL[m.role] && (
                    <span className="hp-mono shrink-0 rounded-[7px] border border-[#38ef8f]/40 px-2 py-[3px] text-[10.5px] font-extrabold text-[#38ef8f]">
                      {ROLE_LABEL[m.role]}
                    </span>
                  )}
                </li>
              ))}
            </ul>
          )}
          {!group.is_virtual && (
            <p className="mt-3 text-[11px] italic leading-snug text-[#cbe0f0]/40">{GROUP_DISCLAIMER}</p>
          )}
        </div>

        <div className="flex flex-col gap-4 lg:gap-[18px]">
          {/* Сезонный зачёт */}
          <div className={cardCls} aria-label="Season standings">
            <div className="mb-4 flex flex-wrap items-baseline justify-between gap-2">
              <h2 className="text-[15px] font-black uppercase tracking-[0.2em] text-[#7dd3fc]">
                Season standings
              </h2>
              {group.season_label && (
                <span className="hp-mono text-[11.5px] font-bold text-[#cbe0f0]/55">
                  {group.season_label}
                </span>
              )}
            </div>
            {group.standings.length === 0 || group.standings.every((s) => s.swims === 0) ? (
              <p className="text-[13px] text-[#cbe0f0]/60">В этом сезоне заплывов нет.</p>
            ) : (
              <div className="overflow-x-auto">
                <table className="w-full border-collapse">
                  <thead>
                    <tr className="border-b border-[#7dd3fc]/20">
                      <th className={`${headCls} text-right`}>#</th>
                      <th className={headCls}>Swimmer</th>
                      <th className={`${headCls} text-right`}>Swims</th>
                      <th className={headCls}>Medals</th>
                      <th className={`${headCls} text-right`}>Points</th>
                      <th className={`${headCls} text-right`}>Best FINA</th>
                    </tr>
                  </thead>
                  <tbody>
                    {group.standings.map((s: HubGroupStanding, i) => (
                      <tr key={s.swimmer_id} className="border-b border-[#7dd3fc]/10">
                        <td className={`${cellCls} hp-mono text-right font-extrabold text-[#cbe0f0]/70`}>
                          {i + 1}
                        </td>
                        <td className={`${cellCls} font-extrabold text-[#f3f8fd]`}>
                          <span>{s.name || s.name_en}</span>
                          {ROLE_LABEL[s.role] && (
                            <span className="hp-mono ml-2 rounded-[6px] border border-[#38ef8f]/40 px-[6px] py-[2px] text-[10px] font-extrabold text-[#38ef8f]">
                              {ROLE_LABEL[s.role]}
                            </span>
                          )}
                        </td>
                        <td className={`${cellCls} hp-mono text-right`}>{s.swims || '—'}</td>
                        <td className={`${cellCls} whitespace-nowrap`}>
                          {s.golds + s.silvers + s.bronzes === 0 ? (
                            <span className="text-[#cbe0f0]/40">—</span>
                          ) : (
                            <span className="hp-mono">
                              {s.golds > 0 && <span className="mr-2">🥇{s.golds}</span>}
                              {s.silvers > 0 && <span className="mr-2">🥈{s.silvers}</span>}
                              {s.bronzes > 0 && <span>🥉{s.bronzes}</span>}
                            </span>
                          )}
                        </td>
                        <td className={`${cellCls} hp-mono text-right font-extrabold text-[#7dd3fc]`}>
                          {s.club_points || '—'}
                        </td>
                        <td className={`${cellCls} hp-mono text-right`}>{s.best_fina || '—'}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </div>

          {/* Рекорды группы */}
          <div id="records" className={cardCls} aria-label="Group records">
            <h2 className="mb-4 text-[15px] font-black uppercase tracking-[0.2em] text-[#7dd3fc]">
              Group records
            </h2>
            {group.bests.length === 0 ? (
              <p className="text-[13px] text-[#cbe0f0]/60">У участников пока нет зачтённых результатов.</p>
            ) : (
              <>
                <div className="overflow-x-auto">
                  <table className="w-full border-collapse">
                    <thead>
                      <tr className="border-b border-[#7dd3fc]/20">
                        <th className={headCls}>Event</th>
                        <th className={headCls}>Pool</th>
                        <th className={headCls}>Time</th>
                        <th className={headCls}>Holder</th>
                        <th className={headCls}>Meet</th>
                        <th className={`${headCls} text-right`}>Pts</th>
                      </tr>
                    </thead>
                    <tbody>
                      {bests.map((b) => (
                        <tr
                          key={`${b.style_name}-${b.distance}-${b.pool_type}-${b.gender}`}
                          className="border-b border-[#7dd3fc]/10"
                        >
                          <td className={`${cellCls} whitespace-nowrap font-extrabold text-[#f3f8fd]`}>
                            {b.distance} {b.style_name}
                            <span className="pl-2 text-[11px] font-bold text-[#cbe0f0]/50">
                              {b.gender === 'female' ? 'W' : b.gender === 'male' ? 'M' : b.gender}
                            </span>
                          </td>
                          <td className={cellCls}>{b.pool_type ?? '—'}</td>
                          <td className={`${cellCls} hp-mono whitespace-nowrap font-extrabold text-[#7dd3fc]`}>
                            {b.time_original}
                          </td>
                          <td className={cellCls}>{b.swimmer_name || b.swimmer_name_en}</td>
                          <td className={`${cellCls} max-w-[220px]`}>
                            <span className="block truncate">{b.competition_name}</span>
                            <span className="text-[11px] text-[#cbe0f0]/50">{b.date}</span>
                          </td>
                          <td className={`${cellCls} hp-mono text-right`}>{b.points || '—'}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
                {group.bests.length > BESTS_PREVIEW_COUNT && (
                  <button
                    type="button"
                    onClick={() => setShowAllBests((v) => !v)}
                    className="mt-3 cursor-pointer rounded-[8px] border border-[#7dd3fc]/40 bg-transparent px-3 py-[6px] text-[12px] font-extrabold text-[#7dd3fc] transition-colors hover:bg-[rgba(56,189,248,0.12)]"
                  >
                    {showAllBests ? 'Show less' : `Show all ${group.bests.length}`}
                  </button>
                )}
              </>
            )}
          </div>

          {/* Галерея группы */}
          <GroupGallery gallery={group.gallery} />

          {/* From members (public-публикации участников) — под Gallery */}
          <FromMembersGallery key={`from-members-${publicationsReloadKey}`} group={group} />

          {/* 🔒 Разборы (members-медиа) — рендерится только участникам */}
          <MembersReviews group={group} />

          {/* Публикации участников (members-слой) — после тренерских разборов */}
          <MembersPublications key={`members-publications-${publicationsReloadKey}`} group={group} />

          {/* Заявки на публикацию — видят только управляющие */}
          <PublicationsInbox group={group} onDecided={() => setPublicationsReloadKey((k) => k + 1)} />

          {/* Последние заплывы */}
          <div className={cardCls} aria-label="Recent swims">
            <h2 className="mb-4 text-[15px] font-black uppercase tracking-[0.2em] text-[#7dd3fc]">
              Recent swims
            </h2>
            {group.recent_results.length === 0 ? (
              <p className="text-[13px] text-[#cbe0f0]/60">Заплывов пока нет.</p>
            ) : (
              <div className="overflow-x-auto">
                <table className="w-full border-collapse">
                  <thead>
                    <tr className="border-b border-[#7dd3fc]/20">
                      <th className={headCls}>Date</th>
                      <th className={headCls}>Swimmer</th>
                      <th className={headCls}>Event</th>
                      <th className={headCls}>Time</th>
                      <th className={headCls}>Pos</th>
                      <th className={`${headCls} max-w-[220px]`}>Meet</th>
                    </tr>
                  </thead>
                  <tbody>
                    {group.recent_results.map((r) => (
                      <tr key={r.id} className="border-b border-[#7dd3fc]/10">
                        <td className={`${cellCls} hp-mono whitespace-nowrap`}>{r.date}</td>
                        <td className={`${cellCls} font-extrabold text-[#f3f8fd]`}>
                          {swimmerDisplayName(r.last_name, r.first_name, r.last_name_en, r.first_name_en)}
                        </td>
                        <td className={`${cellCls} whitespace-nowrap`}>
                          {r.event_style_len} {r.event_style_name}
                          {r.is_relay && <span className="pl-1 text-[11px] text-[#cbe0f0]/50">relay</span>}
                        </td>
                        <td className={`${cellCls} hp-mono whitespace-nowrap font-extrabold ${r.time_fail ? 'text-[#ef5350]' : 'text-[#7dd3fc]'}`}>
                          {r.time_fail ? 'DSQ' : r.time}
                        </td>
                        <td className={`${cellCls} hp-mono`}>{r.position ?? '—'}</td>
                        <td className={`${cellCls} max-w-[220px]`}>
                          <span className="block truncate">{r.competition}</span>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </div>
        </div>
      </section>
    </>
  );
}

// ── Корневой компонент страницы ──────────────────────────────────────────────

function Groups() {
  const slug = useMemo(() => new URLSearchParams(window.location.search).get('group'), []);

  const [groups, setGroups] = useState<HubGroupListItem[]>([]);
  const [favorites, setFavorites] = useState<HubGroupDetails | null>(null);
  const [details, setDetails] = useState<HubGroupDetails | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;

    async function load() {
      try {
        if (slug) {
          // favorites — авторизованный эндпоинт с тем же контрактом
          const url = slug === 'favorites' ? '/api/hub-groups/favorites' : `/api/hub-groups/${encodeURIComponent(slug)}`;
          const r = await fetch(url, { credentials: 'include' });
          if (!r.ok) throw new Error(r.status === 404 ? 'Группа не найдена' : `Ошибка загрузки (${r.status})`);
          const data: HubGroupDetails = await r.json();
          if (!cancelled) setDetails(data);
        } else {
          const [listR, favR] = await Promise.all([
            fetch('/api/hub-groups'),
            // 401 для незалогиненного — норма, карточка избранного просто не показывается
            fetch('/api/hub-groups/favorites', { credentials: 'include' }).catch(() => null),
          ]);
          if (!listR.ok) throw new Error(`Ошибка загрузки (${listR.status})`);
          const list: HubGroupListItem[] = await listR.json();
          const fav: HubGroupDetails | null = favR && favR.ok ? await favR.json() : null;
          if (!cancelled) {
            setGroups(list);
            setFavorites(fav);
          }
        }
      } catch (e) {
        if (!cancelled) setError(e instanceof Error ? e.message : 'Ошибка загрузки');
      } finally {
        if (!cancelled) setLoading(false);
      }
    }

    load();
    return () => {
      cancelled = true;
    };
  }, [slug]);

  return (
    <div className="home-page relative min-h-screen overflow-x-clip pb-[96px] text-[#f3f8fd]">
      <div className="hp-shimmer" aria-hidden="true" />

      <AppTopbar active="groups" />

      {loading && (
        <p className="px-5 pt-10 text-[14px] font-bold text-[#cbe0f0]/60 lg:px-16">Loading…</p>
      )}
      {!loading && error && (
        <div className="px-5 pt-10 lg:px-16">
          <p className="text-[15px] font-bold text-[#ef5350]">{error}</p>
          <a href="./groups.html" className="mt-2 inline-block text-[13px] font-extrabold text-[#7dd3fc] no-underline hover:underline">
            ← All groups
          </a>
        </div>
      )}
      {!loading && !error && (details ? (
        <GroupDetails group={details} />
      ) : (
        <>
          <GroupsList groups={groups} favorites={favorites} />
          <MyGroupsPanel />
        </>
      ))}

      <RecordTicker />
    </div>
  );
}

export default Groups;
