import React, { useState } from 'react';
import UI_SwimmerGallery from '../../components/mix/swimmer-gallery/swimmer-gallery';
import { HelperMedia } from '../../../utils/helpers';
import type { GalleryItem } from '../../../utils/interfaces/results';
import { useSwimmerMedia } from '../use-swimmer-media';
import { PanelEmpty } from './swimmer-panels';

/**
 * Таб Media (BLOCKS.md §9): публичная галерея пловца. Видимость режет СЕРВЕР
 * (/api/swimmers/{id}/media отдаёт аноним — approved public, залогиненному — плюс своё
 * и members своих групп), поэтому клиент не фильтрует.
 *
 * Владельческий блок «My links» тут не рисуется: владельца у пловца в модели нет
 * («Me» — это primary favorite, прав он не даёт), управление ссылками живёт в My media.
 */
function MediaTile({ item, onClick }: { item: GalleryItem; onClick: () => void }) {
  const thumb = HelperMedia.resolveThumbUrl(item.type, item.sourceType ?? 'other', item.url ?? '');
  return (
    <button type="button" onClick={onClick} className="deep-media-tile">
      {thumb ? (
        <img src={thumb} alt="" className="h-full w-full object-cover" />
      ) : (
        <span className="deep-media-tile__glyph">{item.type === 'image' ? '🖼' : '▶'}</span>
      )}
      {item.type === 'video' && <span className="deep-media-tile__play">▶</span>}
    </button>
  );
}

function SwimmerMediaPanel({ swimmerId }: { swimmerId: number }) {
  const media = useSwimmerMedia(swimmerId);
  const [openIndex, setOpenIndex] = useState<number | null>(null);

  if (media.length === 0) {
    return (
      <PanelEmpty>
        No public photos or videos yet. Links added in My media appear here once approved.
      </PanelEmpty>
    );
  }

  return (
    <>
      <div className="deep-panel-head">
        <div>
          <div className="deep-panel-title">Media</div>
          <div className="deep-panel-hint">{media.length} items</div>
        </div>
        <a className="deep-legend" href="/my-media">Manage in My media →</a>
      </div>
      <div className="deep-media-grid">
        {media.map((item, idx) => (
          <MediaTile key={`${item.url}-${idx}`} item={item} onClick={() => setOpenIndex(idx)} />
        ))}
      </div>
      <UI_SwimmerGallery gallery={media} openIndex={openIndex} onClose={() => setOpenIndex(null)} popupSize="lg" />
    </>
  );
}

export default SwimmerMediaPanel;
