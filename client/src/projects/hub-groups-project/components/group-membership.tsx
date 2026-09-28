import React, { useState } from 'react';
import { DeepCtaChip, DeepMenuChip } from '../../components/deep/hero-identity';
import { useLoginModal } from '../../components/login-modal/login-modal-context';
import type { HubGroupDetails } from '../types';

/**
 * Чип членства в шапке группы (хендофф group-club-changes §1): «✓ Member ⋯» → Leave group.
 *
 * Раньше в шапке стояла отдельная кнопка «Leave group» наравне с «Competitions →» и съедала
 * ~330px строки имени на десктопе. Выход — редкое действие, ему место в меню чипа.
 *
 * Состояние членства приходит СВЕРХУ (из страницы), а не своим `useHubGroupMembership`:
 * от него зависят и табы (замок Trainings), и блок тренировок, — второй экземпляр хука
 * после «Join» оставил бы таб закрытым до перезагрузки.
 */

export interface GroupMembershipState {
  isAuthenticated: boolean;
  /** Статус заявки зрителя; null — не участник. */
  status: 'active' | 'pending' | null;
  join: (groupId: number) => Promise<unknown>;
  leave: (groupId: number) => Promise<unknown>;
}

function GroupMembershipChip({ group, membership }: { group: HubGroupDetails; membership: GroupMembershipState }) {
  const { openLoginModal } = useLoginModal();
  const [busy, setBusy] = useState(false);

  // У виртуального «Моё избранное» вступать некуда.
  if (group.is_virtual || group.id <= 0) return null;

  const run = async (action: () => Promise<unknown>) => {
    setBusy(true);
    try { await action(); } finally { setBusy(false); }
  };

  const joinLabel = group.join_policy === 'approval' ? '+ Request to join' : '+ Join group';

  // Гостю — та же кнопка, клик ведёт во вход: фича видна, но требует логина.
  if (!membership.isAuthenticated) {
    return <DeepCtaChip size="member" onClick={openLoginModal} title="Sign in to join this group">{joinLabel}</DeepCtaChip>;
  }

  if (membership.status === 'active') {
    return (
      <DeepMenuChip
        variant="member"
        label="✓ Member"
        busy={busy}
        items={[{ label: 'Leave group', danger: true, onSelect: () => run(() => membership.leave(group.id)) }]}
      />
    );
  }

  if (membership.status === 'pending') {
    return (
      <DeepMenuChip
        variant="member"
        label="Request sent"
        busy={busy}
        items={[{ label: 'Cancel request', danger: true, onSelect: () => run(() => membership.leave(group.id)) }]}
      />
    );
  }

  return (
    <DeepCtaChip size="member" busy={busy} onClick={() => run(() => membership.join(group.id))}>
      {joinLabel}
    </DeepCtaChip>
  );
}

export default GroupMembershipChip;
