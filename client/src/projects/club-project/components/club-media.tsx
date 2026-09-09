import React, { useEffect, useMemo, useState } from 'react';
import UI_SwimmerGallery from '../../components/mix/swimmer-gallery/swimmer-gallery';
import { GalleryItem } from '../../../utils/interfaces/results';
import { HelperMedia } from '../../../utils/helpers';

/**
 * Медиа клуба — одобренные public-публикации участников клуба (`GET /api/clubs/{id}/media`).
 *
 * Клуб и группа это два вида одного (коллектив пловцов), поэтому лента у них устроена
 * одинаково: медиа остаётся личным, а сюда его выводит ПУБЛИКАЦИЯ. Разница лишь в том, что
 * ростер клуба приходит из справочника федерации (`Swimmer.ClubId`), и вести его руками не
 * нужно — план `docs/plans/entity-page-shell-plan.md` §3.10.
 *
 * Уровня `members` тут не бывает: аккаунтов-участников у клуба не существует, поэтому всё,
 * что здесь видно, видно любому посетителю.
 */

interface ClubMediaItem {
  id: number;
  media_type: 'image' | 'video' | 'album';
  source_type: 'youtube' | 'vimeo' | 'album' | 'other';
  url: string;
  swimmer_name?: string | null;
  result_label?: string | null;
}

function ClubMedia({ clubId }: { clubId: number }) {
  const [items, setItems] = useState<ClubMediaItem[] | null>(null);
  const [openIndex, setOpenIndex] = useState<number | null>(null);

  useEffect(() => {
    let alive = true;
    setItems(null);
    fetch(`/api/clubs/${clubId}/media`)
      .then((r) => (r.ok ? (r.json() as Promise<ClubMediaItem[]>) : []))
      .then((data) => { if (alive) setItems(data); })
      .catch(() => { if (alive) setItems([]); });
    return () => { alive = false; };
  }, [clubId]);

  // Лайтбокс — только для image/video: album это внешняя ссылка (target=_blank).
  const { lightbox, indexById } = useMemo(() => {
    const list = (items ?? []).filter((m) => m.media_type !== 'album');
    return {
      lightbox: list.map((m): GalleryItem => ({
        type: m.media_type === 'video' ? 'video' : 'image',
        sourceType: m.source_type === 'album' ? undefined : (m.source_type as GalleryItem['sourceType']),
        url: m.url,
      })),
      indexById: new Map(list.map((m, i) => [m.id, i])),
    };
  }, [items]);

  return (
    <section className="deep-card mb-4" aria-label="Media">
      <div className="deep-card-title">Media</div>
      <div className="deep-card-sub mt-1">
        published by the club's swimmers · visible to everyone
      </div>

      {items == null ? (
        <div className="mt-4 text-[13px] font-bold" style={{ color: 'var(--deep-text-mute)' }}>
          Loading…
        </div>
      ) : items.length === 0 ? (
        <div className="mt-4 text-[13px] font-bold" style={{ color: 'var(--deep-text-mute)' }}>
          Nothing published yet. Swimmers of this club can share their videos from My media —
          each request is approved before it shows up here.
        </div>
      ) : (
        <div className="mt-4 grid grid-cols-3 gap-2 sm:grid-cols-4 min-[960px]:grid-cols-6">
          {items.map((m) => {
            const thumb = HelperMedia.resolveThumbUrl(m.media_type, m.source_type, m.url);
            const caption = [m.swimmer_name, m.result_label].filter(Boolean).join(' · ');

            if (m.media_type === 'album') {
              return (
                <a
                  key={m.id}
                  href={m.url}
                  target="_blank"
                  rel="noopener noreferrer"
                  className="hp-mono flex aspect-square flex-col items-center justify-center gap-1 rounded-[12px] border p-2 text-center text-[12px] font-extrabold no-underline"
                  style={{
                    borderColor: 'var(--deep-accent-border)',
                    background: 'var(--deep-card-bg-row)',
                    color: 'var(--deep-accent)',
                  }}
                >
                  <span className="text-[24px]">📂</span>
                  <span className="line-clamp-2">{caption || 'Album'} ↗</span>
                </a>
              );
            }

            return (
              <button
                key={m.id}
                type="button"
                onClick={() => setOpenIndex(indexById.get(m.id) ?? 0)}
                title={caption || undefined}
                className="relative aspect-square cursor-pointer overflow-hidden rounded-[12px] border p-0"
                style={{ borderColor: 'var(--deep-card-border)', background: 'var(--deep-card-bg-row)' }}
              >
                {thumb
                  ? <img loading="lazy" src={thumb} alt="" className="h-full w-full object-cover" />
                  : <span className="text-[28px]">🎬</span>}
                {m.media_type === 'video' && (
                  <span
                    className="absolute right-1.5 top-1.5 rounded-full px-1.5 py-1 text-[13px] leading-none"
                    style={{ background: 'var(--deep-scrim, rgba(0,0,0,.55))' }}
                  >
                    ▶
                  </span>
                )}
                {caption && (
                  <span
                    className="absolute inset-x-0 bottom-0 truncate px-2 py-1 text-[11px] font-bold"
                    style={{
                      background: 'linear-gradient(0deg, rgba(0,0,0,.72), transparent)',
                      color: '#fff',
                    }}
                  >
                    {caption}
                  </span>
                )}
              </button>
            );
          })}
        </div>
      )}

      <UI_SwimmerGallery gallery={lightbox} openIndex={openIndex} onClose={() => setOpenIndex(null)} />
    </section>
  );
}

export default ClubMedia;
