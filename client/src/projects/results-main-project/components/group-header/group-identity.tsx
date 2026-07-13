import React from 'react';
import UI_ClubIcon from '../../../components/mix/club-icon/club-icon';
import UI_FlagEmoji from '../../../components/mix/flag-icon/flag-icon';
import type { HubGroupDetails } from './types';

// Левая часть GroupHeaderTop: иконка/инициал + имя (RTL-aware) + мета-строка
// (name_en, локация, official-бейдж, тренер, число пловцов).
// Цвет текста наследуется от GroupHeaderTop (var(--theme-mode-accent-text));
// полупрозрачные рамки/фоны — color-mix от того же токена.

interface GroupIdentityProps {
  slug: string;
  group: HubGroupDetails | null;
}

const softBorder = 'color-mix(in srgb, var(--theme-mode-accent-text) 35%, transparent)';
const softBg = 'color-mix(in srgb, var(--theme-mode-accent-text) 15%, transparent)';

export default function GroupIdentity({ slug, group }: GroupIdentityProps) {
  // Тренер — участник с role='coach' (спека: coach опционально в мета-строке).
  const coach = group?.members?.find((m) => m.role === 'coach');

  return (
    <div className="flex min-w-0 items-center gap-3.5">
      {group?.icon_url ? (
        <img
          src={group.icon_url}
          alt=""
          className="h-[52px] w-[52px] flex-none rounded-[14px] border object-cover"
          style={{ borderColor: softBorder }}
        />
      ) : (
        <span
          className="flex h-[52px] w-[52px] flex-none items-center justify-center rounded-[14px] border text-[22px] font-black"
          style={{ borderColor: softBorder, background: 'color-mix(in srgb, var(--theme-mode-accent-text) 20%, transparent)' }}
        >
          {((group?.name ?? slug).trim()[0] ?? '?').toUpperCase()}
        </span>
      )}
      <div className="min-w-0">
        <div className="truncate text-[19px] font-extrabold leading-tight" dir="auto">
          {group?.name ?? slug}
        </div>
        <div className="mt-0.5 flex flex-wrap items-center gap-2 text-[12px] font-semibold opacity-85">
          {group?.name_en && group.name_en !== group.name && <span>{group.name_en}</span>}
          {(group?.country || group?.location) && (
            <span dir="auto" className="inline-flex items-center gap-1.5">
              {/* country — alpha-3 (ISR…), конвертацию в alpha-2 делает сам UI_FlagEmoji */}
              {group?.country && <UI_FlagEmoji countryCode={group.country} size="16x12" />}
              {group?.location}
            </span>
          )}
          {group?.is_official && group.club_name && (
            <span
              className="inline-flex items-center gap-1.5 rounded-full border px-2.5 py-0.5 text-[11px] font-extrabold"
              style={{ borderColor: softBorder, background: softBg }}
            >
              <UI_ClubIcon clubName={group.club_name} iconWidth="4" styleType="icon-notext" />
              Official · {group.club_name}
            </span>
          )}
          {coach && <span dir="auto">Coach: {coach.name_en || coach.name}</span>}
          {(group?.members?.length ?? 0) > 0 && <span>{group!.members.length} swimmers</span>}
        </div>
      </div>
    </div>
  );
}
