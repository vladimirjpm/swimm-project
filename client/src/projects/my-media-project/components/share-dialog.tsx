import { parseTargetKey, targetKey } from '../../../hooks/useUserMedia';
import type { ShareDialogState } from '../use-share-dialog';

/** Share with a group — модал карточки раздела Unlinked (мобильная шторка действий удалена 08.09.2026,
 *  e0fba073). Состояние и отправка — useShareDialog. */
export default function ShareDialog({ share }: { share: ShareDialogState }) {
  const {
    shareTarget, setShareTarget,
    shareTargets,
    shareTargetKey, setShareTargetKey,
    shareLevel, setShareLevel,
    shareBusy, shareError,
    handlePublish,
  } = share;

  if (!shareTarget) return null;

  return (
    <div className="fixed inset-0 z-[100] flex items-center justify-center bg-[var(--t-scrim)] backdrop-blur-[4px]" onClick={() => setShareTarget(null)}>
      <div
        className="w-[420px] max-w-[calc(100vw-40px)] rounded-[16px] border border-[var(--t-border)] bg-[var(--t-surface-strong)] p-5 text-[var(--t-text)]"
        onClick={(e) => e.stopPropagation()}
      >
        <h3 className="m-0 mb-3 text-[15px] font-black">Share with a group or club</h3>
        {shareTargets != null && shareTargets.length === 0 && (
          <p className="text-[12px] text-[var(--t-text-2)]">
            No eligible targets — for a group the swimmer must be in its roster and you must be
            a member; the swimmer's club appears here on its own.
          </p>
        )}
        {shareTargets != null && shareTargets.length > 0 && (
          <div className="flex flex-col gap-2.5">
            <select
              value={shareTargetKey}
              onChange={(e) => {
                setShareTargetKey(e.target.value);
                // У клуба нет аккаунтов-участников, значит и уровня members: сервер такую
                // заявку отвергнет, поэтому не даём выбрать её и в интерфейсе.
                if (parseTargetKey(e.target.value)?.type === 'club') setShareLevel('public');
              }}
              className="rounded-[8px] border border-[var(--t-border)] bg-[var(--t-input-bg)] px-2.5 py-2 text-[12px] text-[var(--t-text)]"
            >
              <option value="">— group or club —</option>
              {shareTargets.map((t) => (
                <option key={targetKey(t)} value={targetKey(t)}>
                  {t.type === 'club' ? `${t.name} (club)` : t.name}
                </option>
              ))}
            </select>
            <select
              value={shareLevel}
              onChange={(e) => setShareLevel(e.target.value as 'members' | 'public')}
              title={parseTargetKey(shareTargetKey)?.type === 'club' ? 'Clubs have no member accounts — publishing to a club is always public' : undefined}
              className="rounded-[8px] border border-[var(--t-border)] bg-[var(--t-input-bg)] px-2.5 py-2 text-[12px] text-[var(--t-text)]"
            >
              <option
                value="members"
                disabled={parseTargetKey(shareTargetKey)?.type === 'club'}
              >
                Group members
              </option>
              <option value="public">Public (visible to everyone)</option>
            </select>
            {shareLevel === 'public' && (
              <p className="m-0 text-[11px] text-[var(--t-warn)]">Public = visible to everyone on the internet after approval.</p>
            )}
            {shareError && <div className="text-[11.5px] text-[var(--t-danger)]">{shareError}</div>}
            <div className="flex justify-end gap-2">
              <button type="button" onClick={() => setShareTarget(null)} className="hp-mono rounded-[8px] border border-[var(--t-border)] bg-transparent px-3 py-[7px] text-[11.5px] font-extrabold text-[var(--t-accent-dim)]">
                Cancel
              </button>
              <button
                type="button"
                disabled={shareBusy || parseTargetKey(shareTargetKey) == null}
                onClick={handlePublish}
                className="hp-mono rounded-[8px] border-none bg-[var(--t-accent)] px-3 py-[7px] text-[11.5px] font-extrabold text-[var(--t-accent-ink)] disabled:opacity-50"
              >
                Submit for approval
              </button>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}
