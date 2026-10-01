import React, { useMemo } from 'react';
import './training-by-session.css';
import UI_SwimmerGallery from '../../components/mix/swimmer-gallery/swimmer-gallery';
import { GalleryItem, TrainingMediaItem } from '../../../utils/interfaces/results';
import { HelperMedia } from '../../../utils/helpers';

/**
 * Медиа-лента тренировки: тайлы + ОДИН лайтбокс на полосу. Одна на журнал тренировок
 * (`training-by-session`) и ленту тренировок группы (`hub-groups-project/components/trainings/feed/`, Е3
 * docs/plans/group-events-media-plan.md, Т4) — чтобы превью, лайтбокс и «album наружу» не
 * разъехались.
 *
 * `onOpen` — клик по тайлу отдаётся наружу (индекс в списке БЕЗ альбомов), а своего лайтбокса
 * полоса не рисует: так таб Trainings группы открывает общий лайтбокс из реестра форм. Без него
 * поведение прежнее (журнал `training-by-session`: свой `UI_SwimmerGallery` на полосу).
 *
 * `onRemove` + `canRemove` — кнопка ✕ на тайле (автор — своё, тренер — любое; решает сервер
 * флагом `can_delete`). Без них лента только для чтения.
 */

type StripItem = Pick<TrainingMediaItem, 'id' | 'media_type' | 'source_type' | 'url' | 'caption'>;

function MediaStripTile({ item, onClick, onRemove }: { item: StripItem; onClick?: () => void; onRemove?: () => void }) {
  const remove = onRemove && (
    <button
      type="button"
      className="tbs-media-remove"
      title="Remove this link"
      aria-label="Remove this link"
      onClick={(e) => { e.stopPropagation(); e.preventDefault(); onRemove(); }}
    >
      ✕
    </button>
  );

  if (item.media_type === 'album') {
    return (
      <a href={item.url} target="_blank" rel="noopener noreferrer" className="tbs-media-tile tbs-media-album">
        <span>📂 {item.caption || 'Album'} ↗</span>
        {remove}
      </a>
    );
  }

  const thumbUrl = HelperMedia.resolveThumbUrl(item.media_type, item.source_type, item.url);

  return (
    <div className="tbs-media-tile" onClick={onClick}>
      {thumbUrl ? <img loading="lazy" src={thumbUrl} referrerPolicy="no-referrer" alt="" /> : <span className="tbs-media-fallback">🎬</span>}
      {item.media_type === 'video' && <span className="tbs-media-play">▶</span>}
      {item.caption && <span className="tbs-media-caption">{item.caption}</span>}
      {remove}
    </div>
  );
}

function TrainingMediaStrip<T extends StripItem>({
  media, className, size = 'md', onRemove, canRemove, onOpen,
}: {
  media: T[];
  /** Доп. класс полосы (у карточки группы свои отступы). */
  className?: string;
  /** Размер плиток: `md` 86×60 (по умолчанию), `lg` 110×72 — лента тренировок на десктопе. */
  size?: 'md' | 'lg';
  onRemove?: (item: T) => void;
  canRemove?: (item: T) => boolean;
  /** Клик по тайлу (кроме альбома); если задан — своего лайтбокса нет. */
  onOpen?: (index: number) => void;
}) {
  const [openIndex, setOpenIndex] = React.useState<number | null>(null);

  // album в лайтбокс не попадает — массив и карта индексов считаются один раз на media.
  const { lightboxGalleryItems, indexById } = useMemo(() => {
    const lightboxItems = media.filter((m) => m.media_type !== 'album');
    return {
      lightboxGalleryItems: lightboxItems.map((m): GalleryItem => ({
        type: m.media_type === 'video' ? 'video' : 'image',
        sourceType: m.source_type === 'album' ? undefined : (m.source_type as GalleryItem['sourceType']),
        url: m.url,
      })),
      indexById: new Map(lightboxItems.map((m, i) => [m.id, i])),
    };
  }, [media]);

  if (media.length === 0) return null;

  return (
    <div className={`tbs-media-strip${size === 'lg' ? ' tbs-media-strip--lg' : ''}${className ? ` ${className}` : ''}`}>
      {media.map((item) => (
        <MediaStripTile
          key={item.id}
          item={item}
          onClick={item.media_type === 'album'
            ? undefined
            : () => {
              const index = indexById.get(item.id) ?? 0;
              if (onOpen) onOpen(index);
              else setOpenIndex(index);
            }}
          onRemove={onRemove && (canRemove?.(item) ?? true) ? () => onRemove(item) : undefined}
        />
      ))}
      {!onOpen && (
        <UI_SwimmerGallery
          gallery={lightboxGalleryItems}
          openIndex={openIndex}
          onClose={() => setOpenIndex(null)}
        />
      )}
    </div>
  );
}

export default TrainingMediaStrip;
