import { useRef, useState } from 'react';
import { useClubOptions } from '../use-my-hub-groups';
import type { ClubSubscriptionPreview } from '../my-groups-types';
import { routes } from '../../../utils/routes';
import { inputCls, btnCls, btnDangerCls, type GroupEdit } from './my-groups-shared';

/**
 * Состав из клуба (П3 плана подписки): группа подписывается на клуб, и её пловцы — все, кто
 * выступал за клуб в этом или прошлом сезоне, — приходят и уходят сами. Живёт внутри редактора
 * состава на ТОМ ЖЕ экземпляре `useMyHubGroupEdit`: иначе после подписки список пловцов ниже
 * остался бы старым до перезагрузки.
 *
 * Предупреждение и подсказку «вступить в существующую» пишет сервер (предпросмотр) — здесь
 * только показ. Подсказка — не запрет (решение Влада): подписаться можно всё равно.
 */
export function ClubSubscriptionBlock({ edit, clubRows, onChanged }: {
  edit: GroupEdit;
  /** Сколько клубных строк уйдёт при отписке (видимые + скрытые). */
  clubRows: number;
  onChanged?: () => void;
}) {
  const clubs = useClubOptions();
  const [clubId, setClubId] = useState<number | ''>('');
  const [preview, setPreview] = useState<ClubSubscriptionPreview | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [confirmUnfollow, setConfirmUnfollow] = useState(false);
  // Предпросмотр грузится на каждый выбор — ответ на прежний выбор не должен перетереть свежий.
  const pickSeq = useRef(0);

  const subscription = edit.data?.clubSubscription ?? null;

  const pick = async (value: number | '') => {
    const seq = ++pickSeq.current;
    setClubId(value);
    setPreview(null);
    setError(null);
    if (value === '') return;
    const p = await edit.previewClubSubscription(value);
    if (seq !== pickSeq.current) return;
    if (p) setPreview(p); else setError('Could not load this club.');
  };

  const follow = async () => {
    if (clubId === '') return;
    setBusy(true);
    setError(null);
    const r = await edit.subscribeToClub(clubId);
    setBusy(false);
    if (!r.success) { setError(r.error ?? 'Could not follow the club.'); return; }
    setClubId('');
    setPreview(null);
    onChanged?.();
  };

  const unfollow = async () => {
    setBusy(true);
    setError(null);
    const r = await edit.unsubscribeFromClub();
    setBusy(false);
    setConfirmUnfollow(false);
    if (r.success) onChanged?.(); else setError(r.error ?? 'Could not unfollow the club.');
  };

  if (subscription) {
    return (
      <div className="flex flex-col gap-2 rounded-[12px] border border-[var(--t-accent-border)] bg-[var(--t-accent-soft)] p-3">
        <p className="m-0 text-[13px] text-[var(--t-text)]">
          Roster from club: <bdi className="font-extrabold">{subscription.clubName}</bdi>
        </p>
        <p className="m-0 text-[11.5px] text-[var(--t-text-2)]">
          Everyone who competed for the club this season or last is in the roster. New swimmers join
          after each competition import; those who stop competing for the club leave on their own.
        </p>
        {confirmUnfollow ? (
          <div className="flex flex-wrap items-center gap-2">
            <span className="text-[12px] font-bold text-[var(--t-warn)]">
              {clubRows} club swimmers will leave the roster, hidden ones too. Swimmers you added yourself stay.
            </span>
            <button type="button" className={btnDangerCls} disabled={busy} onClick={unfollow}>
              {busy ? '…' : 'Unfollow'}
            </button>
            <button type="button" className={btnCls} onClick={() => setConfirmUnfollow(false)}>Cancel</button>
          </div>
        ) : (
          <div>
            <button type="button" className={btnCls} onClick={() => setConfirmUnfollow(true)}>Unfollow club</button>
          </div>
        )}
        {error && <p className="m-0 text-[12.5px] font-bold text-[var(--t-danger)]">{error}</p>}
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-2 rounded-[12px] border border-[var(--t-border)] p-3">
      <p className="m-0 text-[12px] text-[var(--t-text-2)]">
        Roster from a club: everyone who competed for it this season or last joins, and the roster keeps
        itself up to date. Swimmers you add yourself stay either way.
      </p>
      <div className="flex flex-wrap gap-2">
        <select className={`${inputCls} max-w-[260px]`} value={clubId}
          onChange={(e) => pick(e.target.value ? Number(e.target.value) : '')}>
          <option value="">Select a club…</option>
          {clubs.map((c) => (
            <option key={c.id} value={c.id}>{c.name}</option>
          ))}
        </select>
        <button type="button" className={btnCls} disabled={!preview || busy} onClick={follow}>
          {busy ? '…' : 'Follow club'}
        </button>
      </div>
      {preview && (
        <div className="flex flex-col gap-1">
          <p className="m-0 text-[12.5px] text-[var(--t-text)]">
            {preview.swimmerCount} swimmers compete for <bdi className="font-extrabold">{preview.clubName}</bdi> —
            all of them will be in the roster.
          </p>
          {preview.warning && (
            <p className="m-0 text-[11.5px] font-bold text-[var(--t-warn)]">{preview.warning}</p>
          )}
          {preview.hint && preview.hintGroup && (
            <p className="m-0 text-[11.5px] text-[var(--t-text-2)]">
              {preview.hint}{' '}
              <a href={routes.group(preview.hintGroup.slug)}
                className="font-extrabold text-[var(--t-accent)] no-underline hover:underline">
                Open the group →
              </a>
            </p>
          )}
        </div>
      )}
      {error && <p className="m-0 text-[12.5px] font-bold text-[var(--t-danger)]">{error}</p>}
    </div>
  );
}
