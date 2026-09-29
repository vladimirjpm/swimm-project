import React, { useState } from 'react';
import { DeepCtaChip, DeepMenuChip } from '../../components/deep/hero-identity';
import { useLoginModal } from '../../components/login-modal/login-modal-context';
import type { HubGroupDetails } from '../types';
import type { SaveResult } from '../my-groups-types';

/**
 * Чип членства в шапке группы (хендофф group-club-changes §1): «✓ Member ⋯» → Leave group.
 *
 * Раньше в шапке стояла отдельная кнопка «Leave group» наравне с «Competitions →» и съедала
 * ~330px строки имени на десктопе. Выход — редкое действие, ему место в меню чипа.
 *
 * Состояние членства приходит СВЕРХУ (из страницы), а не своим `useHubGroupMembership`:
 * от него зависят и табы (замок Trainings), и блок тренировок, — второй экземпляр хука
 * после «Join» оставил бы таб закрытым до перезагрузки.
 *
 * Отказ сервера (группа заполнена, лимит заявок, самозапись закрыта — HubGroupQuotaRules,
 * rate limit) показывается строкой под чипом: раньше ответ выбрасывался и кнопка молча
 * «не работала».
 */

export interface GroupMembershipState {
  isAuthenticated: boolean;
  /** Статус заявки зрителя; null — не участник. */
  status: 'active' | 'pending' | null;
  join: (groupId: number) => Promise<SaveResult>;
  leave: (groupId: number) => Promise<SaveResult>;
}

function GroupMembershipChip({ group, membership }: { group: HubGroupDetails; membership: GroupMembershipState }) {
  const { openLoginModal } = useLoginModal();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // У виртуального «Моё избранное» вступать некуда.
  if (group.is_virtual || group.id <= 0) return null;

  const run = async (action: () => Promise<SaveResult>) => {
    setBusy(true);
    setError(null);
    try {
      const result = await action();
      if (!result.success) setError(result.error ?? 'Something went wrong. Please try again.');
    } finally { setBusy(false); }
  };

  const withError = (chip: React.ReactElement) => (
    <span className="inline-flex max-w-[280px] flex-col items-start gap-1">
      {chip}
      {error && (
        <span role="alert" className="text-[12px] font-extrabold leading-[1.35]" style={{ color: 'var(--deep-danger)' }}>
          {error}
        </span>
      )}
    </span>
  );

  const joinLabel = group.join_policy === 'approval' ? '+ Request to join' : '+ Join group';

  // Гостю — та же кнопка, клик ведёт во вход: фича видна, но требует логина.
  if (!membership.isAuthenticated) {
    return <DeepCtaChip size="member" onClick={openLoginModal} title="Sign in to join this group">{joinLabel}</DeepCtaChip>;
  }

  if (membership.status === 'active') {
    return withError(
      <DeepMenuChip
        variant="member"
        label="✓ Member"
        busy={busy}
        items={[{ label: 'Leave group', danger: true, onSelect: () => run(() => membership.leave(group.id)) }]}
      />
    );
  }

  if (membership.status === 'pending') {
    return withError(
      <DeepMenuChip
        variant="member"
        label="Request sent"
        busy={busy}
        items={[{ label: 'Cancel request', danger: true, onSelect: () => run(() => membership.leave(group.id)) }]}
      />
    );
  }

  return withError(
    <DeepCtaChip size="member" busy={busy} onClick={() => run(() => membership.join(group.id))}>
      {joinLabel}
    </DeepCtaChip>
  );
}

export default GroupMembershipChip;
