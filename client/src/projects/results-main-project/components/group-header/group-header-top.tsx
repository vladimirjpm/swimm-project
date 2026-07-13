import React from 'react';
import GroupIdentity from './group-identity';
import GroupLinks from './group-links';
import type { HubGroupDetails } from './types';

// Верхний модуль шапки: фон var(--theme-primary), текст var(--theme-mode-accent-text)
// (в dark-режиме акценты светлые — текст темнеет через токен, не хардкодим).
// Только компонует Identity (слева) и Links (справа).

interface GroupHeaderTopProps {
  slug: string;
  group: HubGroupDetails | null;
}

export default function GroupHeaderTop({ slug, group }: GroupHeaderTopProps) {
  return (
    <div
      className="flex flex-wrap items-center gap-3.5 px-5 pb-3.5 pt-3.5"
      style={{ background: 'var(--theme-primary)', color: 'var(--theme-mode-accent-text)' }}
    >
      <GroupIdentity slug={slug} group={group} />
      <GroupLinks links={group?.links ?? []} />
    </div>
  );
}
