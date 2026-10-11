import React from 'react';

/**
 * Липкая панель сохранения редактора дорожек (Т7, Р6 п.2): внизу редактора, `position: sticky`.
 * Телефон — над закреплённой панелью COACH TOOLS каркаса (вторую фиксированную панель не заводим),
 * десктоп — 16px от края. Явный Save остаётся: автосохранения нет, опубликованный план не
 * дёргается у участников на каждом переносе.
 *
 * Состояния: `unsaved` — «● Unsaved changes · Save»; `draft` — «Saved · draft · Publish»;
 * `published` — «Saved · published · Unpublish». Ошибка записи — строкой над панелью.
 */
export type SaveBarState = 'unsaved' | 'draft' | 'published';

function LaneSaveBar({
  state, saving, busy, error, desktop, onSave, onPublish, onUnpublish,
}: {
  state: SaveBarState;
  saving: boolean;
  /** Идёт Publish / Unpublish. */
  busy: boolean;
  error: string | null;
  desktop: boolean;
  onSave: () => void;
  onPublish: () => void;
  onUnpublish: () => void;
}) {
  const primary = 'h-[44px] flex-none cursor-pointer rounded-[12px] border-0 px-[18px] text-[13px] font-extrabold disabled:cursor-not-allowed disabled:opacity-60';
  const working = saving || busy;
  return (
    <div
      className="sticky z-10 mt-3 flex flex-col gap-1.5"
      style={{ bottom: desktop ? 16 : 'calc(96px + env(safe-area-inset-bottom))' }}
    >
      {error && (
        <p className="m-0 px-1 text-[12px] font-bold" style={{ color: 'var(--deep-danger)' }} role="alert">{error}</p>
      )}
      <div
        className="flex min-h-[56px] items-center gap-2.5 rounded-[14px] border"
        style={{
          padding: '8px 8px 8px 14px',
          background: 'var(--t-surface-strong)',
          borderColor: 'var(--deep-ow-border)',
          boxShadow: 'var(--deep-card-shadow)',
        }}
      >
        {state === 'unsaved' && (
          <>
            <span className="mr-auto flex items-center gap-1.5 text-[12.5px] font-extrabold" style={{ color: 'var(--deep-gold)' }}>
              <span aria-hidden="true" className="inline-block flex-none rounded-full" style={{ width: 7, height: 7, background: 'var(--deep-gold-bar)' }} />
              Unsaved changes
            </span>
            <button type="button" className={primary} style={{ background: 'var(--deep-ow)', color: 'var(--deep-accent-ink)' }} onClick={onSave} disabled={working}>
              {saving ? 'Saving…' : 'Save'}
            </button>
          </>
        )}
        {state === 'draft' && (
          <>
            <span className="mr-auto text-[12.5px] font-extrabold" style={{ color: 'var(--t-text-3)' }}>Saved · draft</span>
            <button type="button" className={primary} style={{ background: 'var(--deep-ow)', color: 'var(--deep-accent-ink)' }} onClick={onPublish} disabled={working}>
              {busy ? 'Publishing…' : 'Publish'}
            </button>
          </>
        )}
        {state === 'published' && (
          <>
            <span className="mr-auto text-[12.5px] font-extrabold" style={{ color: 'var(--deep-live)' }}>Saved · published</span>
            <button
              type="button"
              className={`${primary} border`}
              style={{ background: 'transparent', borderColor: 'var(--deep-ow-border)', color: 'var(--deep-ow)' }}
              onClick={onUnpublish}
              disabled={working}
            >
              {busy ? 'Working…' : 'Unpublish'}
            </button>
          </>
        )}
      </div>
    </div>
  );
}

export default LaneSaveBar;
