import React from 'react';
import DeepHeroBand from '../../components/deep/hero-band';
import { DeepBadge } from '../../components/deep/kpi';
import { useLoginModal } from '../../components/login-modal/login-modal-context';
import { useCurrentIdentity } from '../use-my-hub-groups';
import { GroupIcon, JoinButton } from './group-bits';
import type { HubGroupDetails } from '../types';

/**
 * Приватная группа глазами НЕ-участника (решение Влада 11.09.2026, §6-6 плана подписки): вместо
 * 404 — кто это и как вступить. Данных группы здесь нет и быть не может: сервер отдаёт заглушку
 * (`members_only`) без состава, результатов, медиа, расписания и описания.
 *
 * Вступление в приватную — только заявкой, какой бы ни была политика группы: сервер сам ставит
 * `join_policy = approval`, поэтому `JoinButton` говорит «Request to join», а после клика —
 * «Request sent — cancel». Гостю вместо неё — вход.
 */
function GroupMembersOnly({ group }: { group: HubGroupDetails }) {
  const { isAuthenticated } = useCurrentIdentity();
  const { openLoginModal } = useLoginModal();

  return (
    <DeepHeroBand>
      <div className="flex flex-wrap items-start gap-5">
        <GroupIcon iconUrl={group.icon_url} name={group.name_en || group.name} size="lg" />

        <div className="min-w-0 flex-1">
          <h1
            className="truncate text-[34px] leading-tight"
            style={{ fontFamily: 'var(--deep-font-display)', color: 'var(--deep-text)' }}
          >
            {group.name}
          </h1>
          {group.name_en && group.name_en !== group.name && (
            <div className="text-[13px] font-bold" style={{ color: 'var(--deep-text-mute)' }}>
              {group.name_en}
            </div>
          )}

          <div className="mt-3 flex flex-wrap items-center gap-2">
            <DeepBadge>🔒 Private group</DeepBadge>
          </div>

          <p className="mt-3 max-w-[640px] text-[13.5px] leading-[1.55]" style={{ color: 'var(--deep-text)' }}>
            This group is private — only its members can see the roster, results and media.
          </p>
          <p className="mt-1 max-w-[640px] text-[12.5px]" style={{ color: 'var(--deep-text-mute)' }}>
            {isAuthenticated
              ? 'Send a request to join — the group owner decides.'
              : 'Sign in to send a request to join.'}
          </p>

          {/* Кнопка — под текстом, а не третьей колонкой ряда: в ряду она на узком экране
              отнимала у текста всю ширину, и сообщение рассыпалось по слову в строку. */}
          <div className="mt-4 flex flex-wrap items-center gap-2">
            {isAuthenticated ? (
              // JoinButton прижимается вправо (ml-auto) — здесь он в начале строки.
              <div className="flex [&>button]:ml-0"><JoinButton group={group} /></div>
            ) : (
              <button
                type="button"
                onClick={openLoginModal}
                className="hp-mono shrink-0 cursor-pointer rounded-[10px] border-none bg-[var(--t-accent)] px-4 py-2 text-[13px] font-extrabold text-[var(--t-accent-ink)] hover:brightness-110"
              >
                Sign in to request access
              </button>
            )}
          </div>
        </div>
      </div>
    </DeepHeroBand>
  );
}

export default GroupMembersOnly;
