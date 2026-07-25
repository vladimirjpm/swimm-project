import React, { useMemo, useState } from 'react';

/**
 * UI_ClubLogo — лого клуба КРУГОМ фиксированного размера
 * (design_handoff_competition_overview: 26/38/40px, object-fit: cover, border,
 * фолбэк на инициалы клуба на surface-alt). В отличие от UI_ClubIcon (квадрат,
 * object-contain, картинка-плейсхолдер) — единый круглый вид для overview,
 * чтобы разноформатные PNG не «прыгали» в строке.
 *
 * Путь картинки совпадает с UI_ClubIcon; без манифеста — просто пробуем файл и
 * при onError показываем инициалы.
 */
interface UI_ClubLogoProps {
  clubName: string;
  /** Диаметр круга в px (26/38/40 по местам хэндоффа). */
  size: number;
  className?: string;
}

/** Инициалы клуба: первые буквы до двух слов (для иврита — ивритские буквы). */
function clubInitials(name: string): string {
  const words = (name ?? '').trim().split(/\s+/).filter(Boolean);
  if (words.length === 0) return '?';
  if (words.length === 1) return words[0].slice(0, 2).toUpperCase();
  return (words[0][0] + words[1][0]).toUpperCase();
}

const UI_ClubLogo: React.FC<UI_ClubLogoProps> = ({ clubName, size, className = '' }) => {
  const base = import.meta.env.BASE_URL;
  const fileBase = (clubName ?? '')
    .trim()
    .replaceAll(' ', '-')
    .replaceAll('"', '-')
    .replaceAll("'", '-')
    .replaceAll('/', '-')
    .replaceAll('\\', '-')
    .replaceAll('?', '-')
    .replaceAll('#', '-');

  const src = fileBase ? `${base}images/club-icon/${encodeURIComponent(fileBase)}.png` : '';
  const initials = useMemo(() => clubInitials(clubName), [clubName]);
  const [failed, setFailed] = useState(false);

  const frame: React.CSSProperties = {
    width: size,
    height: size,
    borderRadius: '50%',
    border: '1px solid var(--theme-mode-border)',
    background: 'var(--theme-mode-surface-alt, #fafbfd)',
    overflow: 'hidden',
    flex: 'none',
  };

  const showImg = Boolean(src) && !failed;

  return (
    <span
      className={`inline-flex items-center justify-center ${className}`}
      style={frame}
      title={clubName}
    >
      {showImg ? (
        <img
          src={src}
          alt={clubName}
          className="h-full w-full object-cover"
          onError={() => setFailed(true)}
        />
      ) : (
        <span
          className="font-extrabold"
          style={{ fontSize: Math.max(9, Math.round(size * 0.34)), color: 'var(--theme-mode-text-secondary)' }}
        >
          {initials}
        </span>
      )}
    </span>
  );
};

export default UI_ClubLogo;
