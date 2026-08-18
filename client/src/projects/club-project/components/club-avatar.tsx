import React from 'react';

/**
 * Круг с инициалами — общий атом для карточек Top swimmers и Swimmers (роль пловца в
 * зачёте не про клуб, поэтому логотипа тут нет, только инициалы). Цвет по полу
 * (`--deep-male`/`--deep-female`, README §«Правила цвета»); неизвестный пол — нейтральный.
 */

interface Props {
  firstName: string;
  lastName: string;
  gender: string | null;
  size?: number;
}

function ClubAvatar({ firstName, lastName, gender, size = 32 }: Props) {
  const initials =
    `${firstName?.trim().charAt(0) ?? ''}${lastName?.trim().charAt(0) ?? ''}`.toUpperCase() || '?';
  const known = gender === 'male' || gender === 'female';
  const isFemale = gender === 'female';

  return (
    <span
      className="flex shrink-0 items-center justify-center rounded-full border text-[12px] font-black"
      style={{
        width: size,
        height: size,
        background: known
          ? isFemale
            ? 'var(--deep-female-soft)'
            : 'var(--deep-male-soft)'
          : 'var(--deep-card-bg-raised)',
        borderColor: known
          ? isFemale
            ? 'var(--deep-female-border)'
            : 'var(--deep-male-border)'
          : 'var(--deep-card-border)',
        color: known ? (isFemale ? 'var(--deep-female)' : 'var(--deep-male)') : 'var(--deep-text-mute)',
      }}
    >
      {initials}
    </span>
  );
}

export default ClubAvatar;
