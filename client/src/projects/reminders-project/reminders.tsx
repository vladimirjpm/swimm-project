import React, { useCallback, useEffect, useState } from 'react';
import '../home-project/home.css';
import '../components/deep/deep-theme.css';
import { useAuth } from '../../hooks/useAuth';
import { useLoginModal } from '../components/login-modal/login-modal-context';
import { useDeepThemeClass } from '../components/deep/use-deep-theme-class';
import AppTopbar from '../components/app-topbar/app-topbar';
import UI_ModeToggle from '../components/mix/mode-toggle/mode-toggle';
import ConfirmDialog from '../components/confirm-dialog/confirm-dialog';
import { routes } from '../../utils/routes';
import {
  currentSubscription, disablePush, enablePush, notificationPermission, pushSupport,
} from '../../utils/push';
import {
  deleteSubscription, getSettings, getVapidPublicKey, removeDevice, saveSettings, saveSubscription, sendTest,
  type NotificationSettings, type PushDevice, type ReminderLead,
} from './reminders-api';

/**
 * Страница «Reminders» (docs/plans/web-push-reminders-plan.md §8, П1): напоминания о тренировках
 * групп через Web Push — включить на этом устройстве, «Send test», когда напоминать, список
 * устройств. Хендоффа нет (решение Влада 20.10.2026: скромно из того, что есть) — каркас и
 * карточки как у My favorites.
 *
 * Правила браузеров, которые страница держит:
 * - разрешение спрашиваем ТОЛЬКО по нажатию кнопки (iOS иначе не спросит), отказал — объясняем,
 *   как включить в настройках, и больше не дёргаем;
 * - iPhone в обычной вкладке Safari push не умеет: вместо кнопки — как добавить сайт на экран «Домой».
 *
 * Браузер помнит подписку и после выхода из аккаунта: открыл страницу другим аккаунтом —
 * подписка тихо перепривязывается к нему (сервер переносит строку по endpoint).
 */

const LEADS: { value: ReminderLead; label: string }[] = [
  { value: '1h', label: '1 hour before' },
  { value: '2h', label: '2 hours before' },
  { value: 'evening', label: 'Evening before' },
];

function formatWhen(iso: string | null): string {
  if (!iso) return 'never';
  const d = new Date(iso);
  return d.toLocaleString('en-GB', { day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' });
}

function Reminders() {
  const auth = useAuth();
  const { openLoginModal } = useLoginModal();
  const deep = useDeepThemeClass();
  const support = pushSupport();

  const [settings, setSettings] = useState<NotificationSettings | null>(null);
  const [endpoint, setEndpoint] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [note, setNote] = useState<{ tone: 'ok' | 'error'; text: string } | null>(null);
  const [permission, setPermission] = useState(notificationPermission());
  const [pendingRemove, setPendingRemove] = useState<PushDevice | null>(null);

  const reload = useCallback(async (current: string | null) => {
    const s = await getSettings(current);
    setSettings(s);
    return s;
  }, []);

  useEffect(() => {
    if (!auth.isAuthenticated) return;
    let cancelled = false;
    (async () => {
      const sub = support === 'supported' ? await currentSubscription().catch(() => null) : null;
      const current = sub?.endpoint ?? null;
      let s = await reload(current);
      // Браузер подписан, а сервер его за этим аккаунтом не знает (вошёл другой человек,
      // строку удалили как умершую) — перепривязать тихо: разрешение уже дано.
      if (sub && s && s.push_configured && !s.devices.some((d) => d.is_current)) {
        if (!(await saveSubscription(sub))) s = await reload(current);
      }
      if (!cancelled) {
        setEndpoint(current);
        setLoading(false);
      }
    })();
    return () => { cancelled = true; };
  }, [auth.isAuthenticated, reload, support]);

  const run = async (work: () => Promise<string | null>) => {
    setBusy(true);
    setNote(null);
    try {
      const error = await work();
      if (error) setNote({ tone: 'error', text: error });
    } finally {
      setBusy(false);
    }
  };

  const turnOn = () => run(async () => {
    const key = await getVapidPublicKey();
    if (!key) return 'Reminders are not available yet.';
    let sub: PushSubscription;
    try {
      sub = await enablePush(key);
    } catch (e) {
      setPermission(notificationPermission());
      const reason = e instanceof Error ? e.message : '';
      if (reason === 'denied') return null;   // карточка сама покажет, как разрешить
      if (reason === 'dismissed') return 'You closed the permission prompt — tap the button again to allow notifications.';
      return 'This browser could not turn on notifications.';
    }
    setPermission(notificationPermission());
    const error = await saveSubscription(sub);
    if (error) return error;
    setEndpoint(sub.endpoint);
    await reload(sub.endpoint);
    setNote({ tone: 'ok', text: 'Reminders are on for this device. Send a test to check.' });
    return null;
  });

  const turnOff = () => run(async () => {
    const ep = await disablePush().catch(() => endpoint);
    if (ep) await deleteSubscription(ep);
    setEndpoint(null);
    await reload(null);
    setNote({ tone: 'ok', text: 'Reminders are off for this device.' });
    return null;
  });

  const test = () => run(async () => {
    const { result, error } = await sendTest();
    if (error || !result) return error;
    await reload(endpoint);
    if (result.devices === 0) return 'No devices yet — turn reminders on first.';
    const parts = [`Sent to ${result.delivered} of ${result.devices} device${result.devices === 1 ? '' : 's'}.`];
    if (result.removed > 0) parts.push(`${result.removed} expired device${result.removed === 1 ? ' was' : 's were'} removed.`);
    setNote({ tone: result.delivered > 0 ? 'ok' : 'error', text: parts.join(' ') });
    return null;
  });

  const save = (patch: Parameters<typeof saveSettings>[0]) => run(async () => {
    const error = await saveSettings(patch);
    if (!error) await reload(endpoint);
    return error;
  });

  const shell = (children: React.ReactNode) => (
    <div className={`${deep} relative min-h-screen overflow-x-clip pb-24 text-[var(--t-text)]`} style={{ background: 'var(--deep-hero-grad)' }}>
      <AppTopbar />
      {children}
      <UI_ModeToggle />
    </div>
  );

  if (auth.loading || (auth.isAuthenticated && loading)) {
    return <div className="min-h-screen bg-[var(--deep-page-bg)]" />;
  }

  if (!auth.isAuthenticated) {
    return shell(
      <div className="flex min-h-[70vh] items-center justify-center px-4">
        <div className="deep-card w-full max-w-md text-center">
          <h1 className="deep-card-title mb-2">Reminders</h1>
          <p className="mb-4 text-sm text-[var(--t-text-2)]">Sign in to get reminders about your group's trainings</p>
          <button
            type="button"
            onClick={openLoginModal}
            className="hp-mono rounded-[11px] bg-[var(--t-accent)] px-4 py-2 text-sm font-extrabold text-[var(--t-accent-ink)]"
          >
            Sign in
          </button>
        </div>
      </div>,
    );
  }

  const subscribed = endpoint != null && settings?.devices.some((d) => d.is_current) === true;
  const primaryBtn = 'hp-mono rounded-[11px] bg-[var(--t-accent)] px-4 py-2 text-sm font-extrabold text-[var(--t-accent-ink)] disabled:opacity-50';
  const secondaryBtn = 'rounded-[11px] border border-[var(--t-border)] px-4 py-2 text-sm font-bold text-[var(--t-text)] hover:bg-[var(--t-surface)] disabled:opacity-50';
  const card = 'mt-5 rounded-[14px] border border-[var(--t-border)] bg-[var(--t-card)] p-5 sm:p-7';

  const deviceState = (() => {
    if (!settings?.push_configured) {
      return <p className="text-[14px] text-[var(--t-text-2)]">Reminders are not available yet.</p>;
    }
    if (support === 'ios-needs-home-screen') {
      return (
        <div className="text-[14px] text-[var(--t-text-2)]">
          <p className="mb-2 font-bold text-[var(--t-text)]">Add SwimHub to your Home Screen first</p>
          <p className="m-0">
            On iPhone and iPad, notifications work only when SwimHub is opened from the Home Screen:
            in Safari tap <b>Share</b> → <b>Add to Home Screen</b>, open SwimHub from the new icon and
            come back to this page.
          </p>
        </div>
      );
    }
    if (support === 'unsupported') {
      return <p className="text-[14px] text-[var(--t-text-2)]">This browser can't show notifications. Try Chrome, Edge, Firefox or Safari.</p>;
    }
    if (permission === 'denied' && !subscribed) {
      return (
        <div className="text-[14px] text-[var(--t-text-2)]">
          <p className="mb-2 font-bold text-[var(--t-text)]">Notifications are blocked for this site</p>
          <p className="m-0">
            Allow them in the browser: tap the lock icon next to the address (or open the browser's
            site settings), set <b>Notifications</b> to <b>Allow</b>, then reload this page.
          </p>
        </div>
      );
    }
    if (!subscribed) {
      return (
        <div>
          <p className="mb-4 text-[14px] text-[var(--t-text-2)]">
            Get a notification before your group's training — like an app, nothing to install.
          </p>
          <button type="button" className={primaryBtn} disabled={busy} onClick={turnOn}>
            Turn on for this device
          </button>
        </div>
      );
    }
    return (
      <div>
        <p className="mb-4 text-[14px] text-[var(--t-text-2)]">
          <span className="font-bold" style={{ color: 'var(--t-good)' }}>On</span> — this device gets training reminders.
        </p>
        <div className="flex flex-wrap gap-2">
          <button type="button" className={primaryBtn} disabled={busy} onClick={test}>Send test</button>
          <button type="button" className={secondaryBtn} disabled={busy} onClick={turnOff}>Turn off on this device</button>
        </div>
      </div>
    );
  })();

  return shell(
    <main className="mx-auto w-full max-w-[808px] px-4 pt-24">
      <p className="flex items-center gap-1 text-[13px] text-[var(--t-text-3)]">
        <span aria-hidden="true">‹</span> My profile · <bdi>{auth.displayName || auth.email}</bdi>
      </p>
      <h1 className="mt-1.5 mb-1 text-[26px] font-medium text-[var(--t-text)]">Reminders</h1>
      <p className="m-0 text-[13px] text-[var(--t-text-3)]">
        Reminders come for trainings of the <a href={routes.groupsList()} className="text-[var(--t-accent)]">groups</a> you are a member of.
      </p>

      <section className={card}>
        <h2 className="mt-0 mb-3 text-[18px] font-medium">This device</h2>
        {deviceState}
        {note && (
          <p role="status" className="mt-4 mb-0 text-[13px]" style={{ color: note.tone === 'ok' ? 'var(--t-good)' : 'var(--t-danger)' }}>
            {note.text}
          </p>
        )}
      </section>

      {settings && (
        <section className={card}>
          <h2 className="mt-0 mb-3 text-[18px] font-medium">When</h2>
          <div className="flex flex-wrap items-center gap-3">
            <div className="deep-seg" role="group" aria-label="Reminders">
              <button type="button" className={settings.reminders_enabled ? 'active' : ''} disabled={busy}
                aria-pressed={settings.reminders_enabled} onClick={() => save({ reminders_enabled: true })}>On</button>
              <button type="button" className={!settings.reminders_enabled ? 'active' : ''} disabled={busy}
                aria-pressed={!settings.reminders_enabled} onClick={() => save({ reminders_enabled: false })}>Off</button>
            </div>
            {settings.reminders_enabled && (
              <div className="deep-seg" role="group" aria-label="When to remind">
                {LEADS.map((l) => (
                  <button key={l.value} type="button" className={settings.lead === l.value ? 'active' : ''} disabled={busy}
                    aria-pressed={settings.lead === l.value} onClick={() => save({ lead: l.value })}>
                    {l.label}
                  </button>
                ))}
              </div>
            )}
          </div>
          <p className="mt-3 mb-0 text-[12px] text-[var(--t-text-3)]">
            {settings.reminders_enabled
              ? <>“Evening before” comes at 20:00. No reminders between 22:00 and 07:00 — for an early training it comes at 21:00 the evening before. No reminder if you answered “No” or you are on a break.</>
              : <>Reminders are off on all your devices.</>}
          </p>
        </section>
      )}

      {settings && settings.devices.length > 0 && (
        <section className={card}>
          <h2 className="mt-0 mb-2 text-[18px] font-medium">
            Your devices <span className="text-[14px] text-[var(--t-text-3)]">{settings.devices.length}</span>
          </h2>
          <ul className="m-0 list-none p-0">
            {settings.devices.map((d) => (
              <li key={d.id} className="flex items-center gap-3 border-b border-[var(--t-border)] py-2.5 last:border-b-0">
                <div className="min-w-0 flex-1">
                  <div className="truncate text-[14px] font-medium">
                    {d.label}
                    {d.is_current && <span className="ml-2 text-[12px] text-[var(--t-accent-text)]">this device</span>}
                  </div>
                  <div className="text-[12px] text-[var(--t-text-3)]">
                    Added {formatWhen(d.created_at)} · last delivered {formatWhen(d.last_success_at)}
                  </div>
                </div>
                <button
                  type="button"
                  aria-label={`Remove ${d.label}`}
                  title="Remove device"
                  disabled={busy}
                  onClick={() => setPendingRemove(d)}
                  className="flex h-8 w-8 shrink-0 items-center justify-center rounded-full text-[var(--t-text-3)] hover:bg-[var(--t-surface)] hover:text-[var(--t-text)]"
                >
                  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" aria-hidden="true">
                    <path d="M6 6l12 12M18 6L6 18" />
                  </svg>
                </button>
              </li>
            ))}
          </ul>
        </section>
      )}

      {pendingRemove && (
        <ConfirmDialog
          title={<>Remove “{pendingRemove.label}”?</>}
          confirmLabel="Remove"
          busyLabel="Removing…"
          onConfirm={async () => {
            const error = await removeDevice(pendingRemove.id);
            if (!error) {
              if (pendingRemove.is_current) {
                await disablePush().catch(() => null);
                setEndpoint(null);
              }
              await reload(pendingRemove.is_current ? null : endpoint);
              setPendingRemove(null);
            }
            return { success: !error, error: error ?? undefined };
          }}
          onClose={() => setPendingRemove(null)}
        >
          <p className="mt-3 text-[13px] text-[var(--t-text-2)]">
            It stops getting reminders. You can turn them on again from that device.
          </p>
        </ConfirmDialog>
      )}
    </main>,
  );
}

export default Reminders;
