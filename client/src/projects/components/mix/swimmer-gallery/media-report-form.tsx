import React, { useEffect, useState } from 'react';
import {
  MEDIA_REPORT_MAX_COMMENT, MEDIA_REPORT_REASONS, reportMedia, type MediaReportReason,
} from '../../../../hooks/useUserMedia';

/**
 * «⚑ Report» под медиа в лайтбоксе (Р62, docs/data-integrity.md): причина из списка, у «Other» —
 * обязательный текст. После отправки — одинаковое «спасибо» (спрятано ли медиа, зритель не
 * узнаёт). Показывается только залогиненному и не на своём — это решает вызывающий.
 * Сброс при переходе к другому медиа — по `key` у вызывающего.
 */
export default function MediaReportForm({ mediaId }: { mediaId: number }) {
  const [open, setOpen] = useState(false);
  const [reason, setReason] = useState<MediaReportReason | null>(null);
  const [comment, setComment] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [sent, setSent] = useState<null | 'new' | 'again'>(null);

  useEffect(() => { setError(null); }, [reason, comment]);

  if (sent) {
    return (
      <p className="m-0 mt-2 text-xs text-gray-500" role="status">
        {sent === 'again' ? 'You already reported this — thanks, we’ll review it.' : 'Thanks — we’ll review it.'}
      </p>
    );
  }

  if (!open) {
    return (
      <button
        type="button"
        onClick={() => setOpen(true)}
        className="mt-2 border-none bg-transparent p-0 text-xs text-gray-400 hover:text-gray-700 hover:underline"
        title="Report this photo or video to the site admin"
      >
        ⚑ Report
      </button>
    );
  }

  const needsText = reason === 'other';
  const canSend = reason != null && (!needsText || comment.trim().length > 0) && !busy;

  const send = async () => {
    if (!canSend || reason == null) return;
    setBusy(true);
    const r = await reportMedia(mediaId, reason, comment);
    setBusy(false);
    if (r.ok) setSent(r.alreadyReported ? 'again' : 'new');
    else setError(r.error);
  };

  return (
    <form
      className="mt-3 w-full max-w-md rounded-lg border border-gray-200 p-3 text-left text-sm text-gray-800"
      onSubmit={(e) => { e.preventDefault(); send(); }}
      aria-label="Report this media"
    >
      <p className="m-0 mb-2 font-semibold">What’s wrong with it?</p>
      <div className="flex flex-col gap-1">
        {MEDIA_REPORT_REASONS.map((r) => (
          <label key={r.code} className="flex cursor-pointer items-center gap-2">
            <input
              type="radio"
              name={`report-reason-${mediaId}`}
              value={r.code}
              checked={reason === r.code}
              onChange={() => setReason(r.code)}
            />
            {r.label}
          </label>
        ))}
      </div>
      {needsText && (
        <textarea
          value={comment}
          onChange={(e) => setComment(e.target.value)}
          maxLength={MEDIA_REPORT_MAX_COMMENT}
          rows={3}
          placeholder="Tell us what’s wrong"
          className="mt-2 w-full rounded border border-gray-300 p-2 text-sm"
          autoFocus
        />
      )}
      {error && <p className="m-0 mt-2 text-xs text-red-600">{error}</p>}
      <div className="mt-2 flex gap-2">
        <button
          type="submit"
          disabled={!canSend}
          className="rounded bg-gray-800 px-3 py-1 text-xs font-bold text-white disabled:opacity-40"
        >
          {busy ? 'Sending…' : 'Send report'}
        </button>
        <button
          type="button"
          onClick={() => { setOpen(false); setReason(null); setComment(''); }}
          className="rounded border border-gray-300 bg-transparent px-3 py-1 text-xs text-gray-600"
        >
          Cancel
        </button>
      </div>
    </form>
  );
}
