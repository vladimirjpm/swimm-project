import React from 'react';
import { COVER_COLOR, COVER_H, COVER_W } from '../cover-theme';

/** Фон бассейна — свой файл в `public/` (дал Влад): чужая картинка испортила бы экспорт. */
const POOL_BG = `${import.meta.env.BASE_URL}images/covers/pool-background.jpg`;

const FILL: React.CSSProperties = { position: 'absolute', zIndex: -1, left: 0, top: 0, width: COVER_W, height: COVER_H };

/** Фон обложки; `dim` — затемнение поверх него (раскладка C). */
export default function Background({ dim = false }: { dim?: boolean }) {
  return (
    <>
      <img src={POOL_BG} alt="" style={{ ...FILL, objectFit: 'cover', objectPosition: 'center 62%' }} />
      {dim && <div aria-hidden="true" style={{ ...FILL, background: COVER_COLOR.dim }} />}
    </>
  );
}
