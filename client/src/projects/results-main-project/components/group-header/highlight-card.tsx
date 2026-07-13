import React from 'react';
import type { HubGroupHighlight } from './types';

// ЕДИНЫЙ компонент карточки ленты хайлайтов — switch по highlight.type.
// Новый тип карточки («следующий старт», «юбилей» …) = новый case здесь +
// вариант union в HubGroupHighlight; лента и шапка не меняются.
// Вся карточка кликабельна (<a>); внешние URL открываются в новой вкладке.
// Темизация: surface / border-input / text / text-muted — карточки живут на
// светлой ленте между primary-шапкой и primary-табами.

const cardClass =
  'flex min-w-[200px] flex-1 flex-col justify-between gap-1.5 rounded-[10px] border p-2.5 ' +
  'no-underline transition-shadow hover:shadow-md lg:min-w-0';

const cardStyle: React.CSSProperties = {
  background: 'var(--theme-mode-surface)',
  borderColor: 'var(--theme-mode-border-input)',
  color: 'var(--theme-mode-text)',
};

/** Бейдж-пилюля в цвет темы (полупрозрачный primary — виден в обоих режимах). */
function Badge({ children }: { children: React.ReactNode }) {
  return (
    <span
      className="self-start rounded-full px-2 py-0.5 text-[10px] font-extrabold uppercase tracking-wide"
      style={{
        background: 'color-mix(in srgb, var(--theme-primary) 15%, transparent)',
        color: 'var(--theme-primary)',
      }}
    >
      {children}
    </span>
  );
}

/** Превью медиа: картинка или плейсхолдер, если у источника нет превью. */
function Thumb({ src, overlay }: { src?: string | null; overlay?: React.ReactNode }) {
  return (
    <span
      className="relative block h-[54px] w-full overflow-hidden rounded-[7px]"
      style={{ background: 'color-mix(in srgb, var(--theme-mode-text) 10%, transparent)' }}
    >
      {src && <img src={src} alt="" loading="lazy" className="h-full w-full object-cover" />}
      {overlay}
    </span>
  );
}

export default function HighlightCard({ highlight }: { highlight: HubGroupHighlight }) {
  const external = /^https?:\/\//i.test(highlight.url);
  const linkProps = external ? { target: '_blank', rel: 'noopener noreferrer' } : {};

  switch (highlight.type) {
    case 'record':
      return (
        <a href={highlight.url} {...linkProps} className={cardClass} style={cardStyle}>
          <Badge>{highlight.badge}</Badge>
          <span className="text-[12.5px] font-bold leading-tight" dir="auto">{highlight.title}</span>
          <span className="text-[12px] font-semibold" style={{ color: 'var(--theme-mode-text-muted)' }}>
            {highlight.detail}
          </span>
        </a>
      );

    case 'medals':
      return (
        <a href={highlight.url} {...linkProps} className={cardClass} style={cardStyle}>
          <Badge>{highlight.badge}</Badge>
          <span className="flex items-baseline gap-1.5">
            <span className="text-[17px] font-extrabold leading-none">{highlight.place}</span>
            <span className="text-[11px] font-semibold" style={{ color: 'var(--theme-mode-text-muted)' }}>
              {highlight.place_label}
            </span>
          </span>
          <span className="flex gap-2.5 text-[12px] font-bold">
            <span>🥇 {highlight.gold}</span>
            <span>🥈 {highlight.silver}</span>
            <span>🥉 {highlight.bronze}</span>
          </span>
        </a>
      );

    case 'video':
      return (
        <a href={highlight.url} {...linkProps} className={cardClass} style={cardStyle}>
          <Thumb
            src={highlight.thumb_url}
            overlay={
              <>
                {/* Пусковой треугольник поверх превью */}
                <span className="absolute inset-0 flex items-center justify-center text-[20px] text-white drop-shadow">▶</span>
                {highlight.duration && (
                  <span className="absolute bottom-1 right-1 rounded bg-black/70 px-1 text-[10px] font-bold text-white">
                    {highlight.duration}
                  </span>
                )}
              </>
            }
          />
          <span className="truncate text-[12px] font-bold" dir="auto">{highlight.label}</span>
        </a>
      );

    case 'photo':
      return (
        <a href={highlight.url} {...linkProps} className={cardClass} style={cardStyle}>
          <Thumb
            src={highlight.thumb_url}
            overlay={
              highlight.extra ? (
                <span className="absolute bottom-1 right-1 rounded bg-black/70 px-1 text-[10px] font-bold text-white">
                  {highlight.extra}
                </span>
              ) : undefined
            }
          />
          <span className="truncate text-[12px] font-bold" dir="auto">{highlight.label}</span>
        </a>
      );

    default:
      // Неизвестный type от будущего сервера — молча пропускаем (forward-compat).
      return null;
  }
}
