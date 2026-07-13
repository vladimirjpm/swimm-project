import React from 'react';
import type { HubGroupLink } from '../../../hub-groups-project/types';

// Правая часть GroupHeaderTop: чипы внешних ссылок группы (WhatsApp / Instagram / Site …).
// Новый вид ссылки достаточно добавить в GROUP_LINK_LABEL — иначе покажется сырой kind.

const GROUP_LINK_LABEL: Record<string, string> = {
  whatsapp: 'WhatsApp',
  telegram: 'Telegram',
  instagram: 'Instagram',
  site: 'Site',
};

export default function GroupLinks({ links }: { links: HubGroupLink[] }) {
  if (!links.length) return null;
  return (
    <div className="ml-auto flex flex-wrap gap-1.5">
      {links.map((l) => (
        <a
          key={l.kind + l.url}
          href={l.url}
          target="_blank"
          rel="noopener noreferrer"
          className="rounded-full border px-3 py-[5px] text-[11.5px] font-extrabold no-underline transition-opacity hover:opacity-80"
          style={{
            color: 'inherit',
            borderColor: 'color-mix(in srgb, var(--theme-mode-accent-text) 30%, transparent)',
            background: 'color-mix(in srgb, var(--theme-mode-accent-text) 15%, transparent)',
          }}
        >
          {GROUP_LINK_LABEL[l.kind] ?? l.kind} ↗
        </a>
      ))}
    </div>
  );
}
