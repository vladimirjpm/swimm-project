import React from 'react';
import type { CoverData, CoverOptions } from '../cover-data';
import { COVER_COLOR } from '../cover-theme';
import NameRow, { type NameSpec } from './name-row';

const PHOTO = { radius: 28, gap: 14 } as const;
const NAME: NameSpec = { logo: 54, size: 44 };

const SLOT: React.CSSProperties = { flex: '1 1 0', minHeight: 0, alignSelf: 'stretch', borderRadius: PHOTO.radius };

/**
 * Рамка «выберите фото» — только для предпросмотра: `data-cover-skip` велит экспорту её
 * пропустить (`cover-export.ts`), в файл заглушка не попадает.
 */
function PhotoPlaceholder() {
  return (
    <div
      data-cover-skip="1"
      style={{
        ...SLOT, boxSizing: 'border-box', border: `3px dashed ${COVER_COLOR.photoFrame}`,
        display: 'flex', alignItems: 'center', justifyContent: 'center',
        fontSize: 22, fontWeight: 800, letterSpacing: '0.14em',
      }}
    >
      CHOOSE A PHOTO
    </div>
  );
}

function Photo({ src }: { src: string }) {
  return (
    <div style={{ ...SLOT, overflow: 'hidden' }}>
      <img src={src} alt="" style={{ display: 'block', width: '100%', height: '100%', objectFit: 'cover' }} />
    </div>
  );
}

/** Правая колонка раскладок B и C: необязательное фото и имя под ним. */
export default function PhotoAndName({ data, options }: { data: CoverData; options: CoverOptions }) {
  return (
    <div style={{ flex: '1 1 0', minWidth: 0, display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'flex-end', gap: PHOTO.gap }}>
      {options.photoSlot && (options.photo ? <Photo src={options.photo} /> : <PhotoPlaceholder />)}
      {options.showName && <NameRow data={data} spec={NAME} />}
    </div>
  );
}
