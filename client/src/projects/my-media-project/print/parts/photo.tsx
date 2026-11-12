import React from 'react';
import { PRINT_SKIP_ATTR } from '../print-export';
import { PRINT_COLOR } from '../print-theme';

/**
 * Рамка «выберите фото» — только для предпросмотра: экспорт её пропускает (`PRINT_SKIP_ATTR`),
 * в файл заглушка не попадает. Размер и положение задаёт вызывающий через `style`.
 */
export function PhotoPlaceholder({ style, fontSize = 22 }: { style: React.CSSProperties; fontSize?: number }) {
  return (
    <div
      {...PRINT_SKIP_ATTR}
      style={{
        boxSizing: 'border-box', border: `3px dashed ${PRINT_COLOR.photoFrame}`,
        display: 'flex', alignItems: 'center', justifyContent: 'center',
        fontSize, fontWeight: 800, letterSpacing: '0.14em', ...style,
      }}
    >
      CHOOSE A PHOTO
    </div>
  );
}

/** Фото пловца, обрезанное по рамке слота. */
export function Photo({ src, style }: { src: string; style: React.CSSProperties }) {
  return (
    <div style={{ overflow: 'hidden', ...style }}>
      <img src={src} alt="" style={{ display: 'block', width: '100%', height: '100%', objectFit: 'cover' }} />
    </div>
  );
}

/** Слот фото: выбранное фото или рамка-подсказка предпросмотра. */
export default function PhotoSlot({ photo, style, hintSize }: {
  photo: string | null;
  style: React.CSSProperties;
  hintSize?: number;
}) {
  return photo ? <Photo src={photo} style={style} /> : <PhotoPlaceholder style={style} fontSize={hintSize} />;
}
