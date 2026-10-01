import React from 'react';

/**
 * Кружок человека на доске и в редакторе дорожек (Т7): первая буква имени на фоне цвета уровня
 * дорожки (`levelColor` — данные тренера, не тема); нет уровня — `--deep-text-mute`.
 * Текст — `--deep-accent-ink`, как у `PoolCircle` вида Next.
 */
function LaneCircle({
  name, color, size = 26,
}: {
  name: string;
  /** Цвет уровня; null — уровня нет. */
  color: string | null;
  size?: 24 | 26;
}) {
  const letter = (name.trim()[0] ?? '?').toUpperCase();
  return (
    <span
      aria-hidden="true"
      className="inline-flex flex-none items-center justify-center rounded-full text-[10px] font-black leading-none"
      style={{
        width: size, height: size, background: color ?? 'var(--deep-text-mute)', color: 'var(--deep-accent-ink)',
      }}
    >
      {letter}
    </span>
  );
}

export default LaneCircle;
