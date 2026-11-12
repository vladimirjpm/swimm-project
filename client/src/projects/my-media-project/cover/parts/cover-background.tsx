import React from 'react';
import Background from '../../print/parts/background';
import { COVER_BG_FOCUS, COVER_COLOR, COVER_H, COVER_W } from '../cover-theme';

/** Фон обложки; `dim` — затемнение поверх него (раскладка C). */
export default function CoverBackground({ dim = false }: { dim?: boolean }) {
  return <Background width={COVER_W} height={COVER_H} focus={COVER_BG_FOCUS} dim={dim ? COVER_COLOR.dim : undefined} />;
}
