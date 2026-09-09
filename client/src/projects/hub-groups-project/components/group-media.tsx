import React, { useEffect, useMemo, useState } from 'react';
import UI_SwimmerGallery from '../../components/mix/swimmer-gallery/swimmer-gallery';
import { GalleryItem } from '../../../utils/interfaces/results';
import { HelperMedia } from '../../../utils/helpers';
import { useCurrentIdentity } from '../use-my-hub-groups';
import { SwimContextLine } from './group-bits';
import type {
  GroupPublicationItem, HubGroupDetails, HubGroupMediaItem, HubGroupMemberMediaItem,
} from '../types';

/**
 * Медиа-слой группы: галерея, тренерские разборы (members) и публикации участников
 * (public + members). Переехало из `groups.tsx` при разделении списка и страницы (этап C).
 *
 * Устройство слоя и правила видимости — `docs/media-page.md`; здесь только вёрстка и
 * загрузка. Оболочка карточек — общая `.deep-card` вместо прежней `hp-card-std`: страница
 * группы теперь живёт в теме deep, как клуб и пловец.
 */

// ── Галерея группы ───────────────────────────────────────────────────────────

function GalleryTile({ item, onClick }: { item: HubGroupMediaItem; onClick?: () => void }) {
  const captionOverlay = item.caption && (
    <div className="absolute inset-x-0 bottom-0 truncate bg-[linear-gradient(0deg,var(--t-scrim),transparent)] px-2 py-1 text-[11px] font-bold text-[var(--t-text)]">
      {item.caption}
    </div>
  );

  if (item.media_type === 'album') {
    return (
      <a
        href={item.url}
        target="_blank"
        rel="noopener noreferrer"
        className="hp-mono flex aspect-square flex-col items-center justify-center gap-1 rounded-[12px] border border-[var(--t-accent-border)] bg-[var(--t-input-bg)] p-2 text-center text-[12px] font-extrabold text-[var(--t-accent)] no-underline hover:border-[var(--t-accent)] hover:bg-[var(--t-accent-soft)]"
      >
        <span className="text-[24px]">📂</span>
        <span className="line-clamp-2">{item.caption || 'Album'} ↗</span>
      </a>
    );
  }

  const thumbUrl = HelperMedia.resolveThumbUrl(item.media_type, item.source_type, item.url);

  return (
    <div
      className="relative aspect-square cursor-pointer overflow-hidden rounded-[12px] border border-[var(--t-border)] bg-[var(--t-input-bg)]"
      onClick={onClick}
    >
      {thumbUrl ? (
        <img loading="lazy" src={thumbUrl} alt="" className="h-full w-full object-cover" />
      ) : (
        <div className="flex h-full w-full items-center justify-center text-[28px]">🎬</div>
      )}
      {item.media_type === 'video' && (
        <span className="absolute right-1.5 top-1.5 rounded-full bg-[var(--t-scrim)] px-1.5 py-1 text-[13px] leading-none">
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
    <div id="gallery" className="deep-card" aria-label="Gallery">
      <h2 className="deep-card-title mb-4">Gallery</h2>
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
    <div id="members-reviews" className="deep-card" aria-label="Members reviews">
      <h2 className="deep-card-title mb-1">🔒 Reviews</h2>
      <p className="deep-card-sub mb-4 italic">Visible to group members only.</p>
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
                <p className="m-0 truncate text-[13.5px] font-extrabold text-[var(--t-text)]">{item.swimmer_name}</p>
              )}
              {item.result_label && (
                <SwimContextLine
                  label={item.result_label}
                  competitionId={item.competition_id}
                  resultId={item.result_id}
                  swimmerId={item.swimmer_id}
                  className="text-[12px] text-[var(--t-accent-dim)]"
                />
              )}
              {item.caption && (
                <p className="m-0 truncate text-[12px] text-[var(--t-text-2)]">{item.caption}</p>
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
    <div id="from-members" className="deep-card" aria-label="From members (everyone)">
      {/* Обе секции «From members» подписаны уровнем — иначе по экрану не отличить, кто
          увидит поданное видео. Слова и глобус те же, что у уровней публикации в My media
          (`members` / `everyone 🌐`): один словарь на продукт, а не свой на каждой странице. */}
      <h2 className="deep-card-title mb-1">🌐 From members</h2>
      <p className="deep-card-sub mb-4 italic">Visible to everyone.</p>
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
    <div id="members-publications" className="deep-card" aria-label="From members (members only)">
      {/* Замок и подпись обязательны: рядом на странице живёт публичная секция с ТЕМ ЖЕ
          заголовком, и без пометки по экрану не понять, кто увидит поданное видео.
          Форма пометки — как у соседней «🔒 Reviews», второго диалекта тут заводить нельзя. */}
      <h2 className="deep-card-title mb-1">🔒 From members</h2>
      <p className="deep-card-sub mb-4 italic">Visible to group members only.</p>
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


export { GalleryTile, GroupGallery, MembersReviews, FromMembersGallery, MembersPublications };
