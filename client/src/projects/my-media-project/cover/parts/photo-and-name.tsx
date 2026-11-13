import React from 'react';
import NameRow, { type NameSpec } from '../../print/parts/name-row';
import PhotoSlot from '../../print/parts/photo';
import type { CoverData, CoverOptions } from '../cover-data';

const PHOTO = { radius: 28, gap: 14 } as const;
const NAME: NameSpec = { logo: 54, size: 44 };

const SLOT: React.CSSProperties = { flex: '1 1 0', minHeight: 0, alignSelf: 'stretch', borderRadius: PHOTO.radius };

/** Правая колонка раскладок B и C: необязательное фото и имя под ним. */
export default function PhotoAndName({ data, options }: { data: CoverData; options: CoverOptions }) {
  return (
    <div style={{ flex: '1 1 0', minWidth: 0, display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'flex-end', gap: PHOTO.gap }}>
      {options.photoSlot && <PhotoSlot photo={options.photo} style={SLOT} />}
      {options.showName && <NameRow swimmerName={data.swimmerName} clubName={data.clubName} spec={NAME} />}
    </div>
  );
}
