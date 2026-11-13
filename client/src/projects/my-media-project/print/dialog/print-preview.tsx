import React, { useLayoutEffect, useRef, useState } from 'react';
import { PRINT_COLOR } from '../print-theme';

/**
 * Рамка предпросмотра в пропорции картинки. Внутри — настоящая картинка полного размера
 * (`children`), уменьшенная transform-ом под ширину рамки: предпросмотр и файл — одна и та же
 * вёрстка. Пока картинка не готова (`ready` — шрифты ещё грузятся), вместо неё `placeholder`.
 * `maxWidth` — потолок ширины рамки: высокий плакат иначе занял бы весь диалог.
 */
export default function PrintPreview({ width, height, maxWidth, ready, placeholder, children }: {
  width: number;
  height: number;
  maxWidth?: number;
  ready: boolean;
  placeholder: string;
  children: React.ReactNode;
}) {
  const frameRef = useRef<HTMLDivElement>(null);
  const [scale, setScale] = useState(0);

  useLayoutEffect(() => {
    const frame = frameRef.current;
    if (!frame) return undefined;
    const fit = () => setScale(frame.clientWidth / width);
    fit();
    const observer = new ResizeObserver(fit);
    observer.observe(frame);
    return () => observer.disconnect();
  }, [width]);

  return (
    <div
      ref={frameRef}
      className="relative mx-auto w-full shrink-0 overflow-hidden rounded-[10px] border border-[var(--t-border)]"
      style={{ aspectRatio: `${width} / ${height}`, maxWidth, background: PRINT_COLOR.navy }}
    >
      {ready && scale > 0 ? (
        <div style={{ width, height, transform: `scale(${scale})`, transformOrigin: '0 0' }}>
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
