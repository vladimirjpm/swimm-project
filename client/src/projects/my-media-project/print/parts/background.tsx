import React from 'react';

/** Фон бассейна — свой файл в `public/` (дал Влад): чужая картинка испортила бы экспорт. */
const POOL_BG = `${import.meta.env.BASE_URL}images/covers/pool-background.jpg`;

/**
 * Фон картинки во весь её размер. `focus` — какая часть снимка остаётся в кадре
 * (`object-position`); `dim` — цвет затемнения поверх фона, если оно нужно.
 */
export default function Background({ width, height, focus, dim }: {
  width: number;
  height: number;
  focus: string;
  dim?: string;
}) {
  const fill: React.CSSProperties = { position: 'absolute', zIndex: -1, left: 0, top: 0, width, height };
  return (
    <>
      <img src={POOL_BG} alt="" style={{ ...fill, objectFit: 'cover', objectPosition: focus }} />
      {dim && <div aria-hidden="true" style={{ ...fill, background: dim }} />}
    </>
  );
}
