import React, { useEffect, useState } from 'react';
import ConfirmDialog from '../../components/confirm-dialog/confirm-dialog';
import { routes } from '../../../utils/routes';
import { deleteAccount, fetchDeletionImpact, type AccountDeletionImpact } from '../account-api';

/**
 * Окно «Delete account» на /setup-profile (docs/plans/account-deletion-plan.md, У3).
 *
 * Решения Влада 23.10.2026: удаление сразу и насовсем (Р1); свои группы без других людей уходят
 * вместе с аккаунтом, с людьми — блокируют (Р2); подтверждение — свой email плюс пароль или,
 * у аккаунта только с Google, вход не старше 10 минут (Р3); добавленное в чужие группы остаётся
 * без автора (Р4); админ сайта удалиться здесь не может (Р6). Правило — на сервере
 * (`AccountDeletionRules`), окно показывает его ответ и ничего не решает само.
 *
 * Оболочка — общий `ConfirmDialog`, как у удаления группы.
 */
export default function DeleteAccountDialog({ email, onDeleted, onClose }: {
  /** Email аккаунта — подсказка к полю подтверждения. */
  email: string;
  onDeleted: () => void;
  onClose: () => void;
}) {
  const [impact, setImpact] = useState<AccountDeletionImpact | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [typedEmail, setTypedEmail] = useState('');
  const [password, setPassword] = useState('');

  useEffect(() => {
    let alive = true;
    void fetchDeletionImpact().then((i) => {
      if (!alive) return;
      if (i) setImpact(i);
      else setLoadError('Could not check your account — please reload the page.');
    });
    return () => { alive = false; };
  }, []);

  const blocked = (impact?.blockReasons.length ?? 0) > 0;
  const needsReauth = impact != null && !impact.hasPassword && !impact.signInFresh;
  const emailOk = typedEmail.trim().toLowerCase() === email.trim().toLowerCase();
  const canDelete = impact != null && !blocked && !needsReauth && emailOk
    && (!impact.hasPassword || password.length > 0);

  // После повторного входа Google вернёт сюда же, и окно откроется само (`?delete=1`).
  const reauthHref = `/auth/login/google?reauth=true&returnUrl=${encodeURIComponent(`${routes.setupProfile()}?delete=1`)}`;

  return (
    <ConfirmDialog
      title="Delete your account?"
      confirmLabel="Delete my account"
      busyLabel="Deleting…"
      confirmDisabled={!canDelete}
      onConfirm={async () => {
        const error = await deleteAccount(typedEmail, impact?.hasPassword ? password : null);
        if (error) return { success: false, error };
        onDeleted();
        return { success: true };
      }}
      onClose={onClose}
    >
      {loadError && <p className="mt-3 text-[13px] font-bold text-[var(--t-danger)]" role="alert">{loadError}</p>}
      {!impact && !loadError && <p className="mt-3 text-[13px] text-[var(--t-text-2)]">Checking what will be deleted…</p>}

      {impact && blocked && (
        <div className="mt-3 flex flex-col gap-2 text-[13px]" role="alert">
          {impact.blockMessages.map((m) => <p key={m} className="m-0 font-bold text-[var(--t-danger)]">{m}</p>)}
          {impact.sharedGroups.length > 0 && (
            <ul className="m-0 flex list-disc flex-col gap-1 pl-5">
              {impact.sharedGroups.map((g) => (
                <li key={g.id}>
                  <a href={routes.group(g.slug)} className="font-semibold text-[var(--t-accent)]"><bdi>{g.name}</bdi></a>
                  {' '}— {count(g.otherPeople, 'other member')}
                </li>
              ))}
            </ul>
          )}
          <p className="m-0 text-[12.5px] text-[var(--t-text-2)]">
            Need help? Write to <a href="mailto:privacy@swimhub.co.il" className="text-[var(--t-accent)]">privacy@swimhub.co.il</a>.
          </p>
        </div>
      )}

      {impact && !blocked && (
        <>
          <ImpactList impact={impact} />
          <p className="mt-3 text-[13px] text-[var(--t-text-2)]">
            Competition results and swimmer pages stay — they come from official protocols.
            {impact.groupContentKept > 0 && ' Links and trainings you added to other groups stay there without your name.'}
            {' '}If you sign in again later, you start with a new empty account.
          </p>
          <p className="mt-3 text-[12.5px] font-bold text-[var(--t-danger)]">This cannot be undone.</p>

          {needsReauth ? (
            <div className="mt-4 rounded-[10px] border border-[var(--t-border)] p-3 text-[13px]">
              <p className="m-0 mb-2 text-[var(--t-text-2)]">For your security, sign in with Google again to confirm.</p>
              <a
                href={reauthHref}
                className="inline-flex h-8 items-center rounded-[8px] bg-[var(--t-accent)] px-3 text-[13px] font-bold text-[var(--t-accent-ink)] no-underline hover:opacity-90"
              >
                Sign in again
              </a>
            </div>
          ) : (
            <>
              <label className="mt-4 block">
                <span className="mb-1.5 block text-[12px] text-[var(--t-text-2)]">
                  Type your email to confirm: <b className="text-[var(--t-text)]">{email}</b>
                </span>
                <input
                  value={typedEmail}
                  onChange={(e) => setTypedEmail(e.target.value)}
                  type="email"
                  autoComplete="off"
                  spellCheck={false}
                  aria-label="Your email"
                  className={INPUT}
                />
              </label>
              {impact.hasPassword && (
                <label className="mt-3 block">
                  <span className="mb-1.5 block text-[12px] text-[var(--t-text-2)]">Password</span>
                  <input
                    value={password}
                    onChange={(e) => setPassword(e.target.value)}
                    type="password"
                    autoComplete="current-password"
                    aria-label="Password"
                    className={INPUT}
                  />
                </label>
              )}
            </>
          )}
        </>
      )}
    </ConfirmDialog>
  );
}

const INPUT = 'w-full rounded-[10px] border border-[var(--t-accent-border)] bg-[var(--t-input-bg)] px-3 py-2 text-[13px] text-[var(--t-text)] outline-none focus:border-[var(--t-accent)]';

/** Только ненулевое — как в окне удаления группы. */
function ImpactList({ impact: i }: { impact: AccountDeletionImpact }) {
  const lines: React.ReactNode[] = [];
  if (i.favorites) lines.push(`${count(i.favorites, 'favorite')} (swimmers, clubs, family)`);
  if (i.media) {
    lines.push(`${count(i.media, 'media link')}${i.publishedMedia ? ` — ${i.publishedMedia} shared will disappear from groups and pages too` : ''}`);
  }
  if (i.reactions) lines.push(count(i.reactions, 'reaction'));
  if (i.startListPlans) lines.push(count(i.startListPlans, 'start list plan'));
  if (i.groupMemberships) lines.push(`Membership in ${count(i.groupMemberships, 'group')}`);
  if (i.groupAdminships) lines.push(`Admin rights in ${count(i.groupAdminships, 'group')}`);
  if (i.pushDevices) lines.push(`Notifications on ${count(i.pushDevices, 'device')}`);
  for (const g of i.groupsDeleted) {
    lines.push(<>Your group <bdi className="font-semibold">{g.name}</bdi>{g.trainingSessions ? ` with ${count(g.trainingSessions, 'training')}` : ''}</>);
  }

  return (
    <>
      <p className="mt-3 text-[13px] text-[var(--t-text-2)]">
        {lines.length ? 'This deletes your sign-in details and:' : 'This deletes your sign-in details. Nothing else is linked to your account.'}
      </p>
      {lines.length > 0 && (
        <ul className="mt-1.5 flex list-disc flex-col gap-1 pl-5 text-[13px]">
          {lines.map((line, n) => <li key={n}>{line}</li>)}
        </ul>
      )}
    </>
  );
}

function count(n: number, noun: string): string {
  return `${n} ${n === 1 ? noun : `${noun}s`}`;
}
