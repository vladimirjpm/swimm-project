import React, { useLayoutEffect, useRef, useState } from 'react';
import { COVER_COLOR, COVER_H, COVER_W } from '../cover-theme';

/**
 * Рамка предпросмотра 16:9. Внутри — настоящая обложка 1280×720 (`children`), уменьшенная
 * transform-ом под ширину рамки: предпросмотр и файл — одна и та же вёрстка. Пока обложка не
 * готова (`ready` — шрифты ещё грузятся), вместо неё строка `placeholder`.
 */
export default function CoverPreview({ ready, placeholder, children }: {
  ready: boolean;
  placeholder: string;
  children: React.ReactNode;
}) {
  const frameRef = useRef<HTMLDivElement>(null);
  const [scale, setScale] = useState(0);

  useLayoutEffect(() => {
    const frame = frameRef.current;
    if (!frame) return undefined;
    const fit = () => setScale(frame.clientWidth / COVER_W);
    fit();
    const observer = new ResizeObserver(fit);
    observer.observe(frame);
    return () => observer.disconnect();
  }, []);

  return (
    <div
      ref={frameRef}
      className="relative w-full shrink-0 overflow-hidden rounded-[10px] border border-[var(--t-border)]"
      style={{ aspectRatio: `${COVER_W} / ${COVER_H}`, background: COVER_COLOR.navy }}
    >
      {ready && scale > 0 ? (
        <div style={{ width: COVER_W, height: COVER_H, transform: `scale(${scale})`, transformOrigin: '0 0' }}>
          {children}
        </div>
      ) : (
        <div className="absolute inset-0 flex items-center justify-center text-[12px] font-bold text-white/70">
          {placeholder}
        </div>
      )}
    </div>
  );
}
