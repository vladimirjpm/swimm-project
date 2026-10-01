import React, { useRef, useState } from 'react';
import { DeepSheet, SheetButton, SheetError, SheetFooter } from '../../../../components/deep/deep-sheet';
import { sessionLabel } from '../../../use-training-sessions';
import { useTrainingsTab } from '../trainings-context';
import type { TrainingsSheetProps } from '../trainings-sheets';
import { formatSheetDate } from './link-helpers';

/**
 * Удаление тренировки (диалог по центру и на телефоне): сводка «что пропадёт» и два исхода.
 * Фокус при открытии — на Cancel, чтобы рефлекторный Enter ничего не удалил. Видео по ссылкам
 * остаются там, где лежат (YouTube, Google Photos): сайт хранил только ссылки.
 */
function DeleteTrainingSheet({ sheet, onClose }: TrainingsSheetProps<'delete-training'>) {
  const { sessions } = useTrainingsTab();
  const { session } = sheet;
  const [error, setError] = useState<string | null>(null);
  const cancelRef = useRef<HTMLButtonElement>(null);

  const links = session.media.length;
  const meta = [formatSheetDate(session.date), session.start, links > 0 ? `${links} ${links === 1 ? 'link' : 'links'}` : null]
    .filter(Boolean).join(' · ');

  const remove = async () => {
    setError(null);
    const e = await sessions.remove(session.id);
    if (e) setError(e);
    else onClose();
  };

  return (
    <DeepSheet
      title="Delete training?"
      variant="dialog"
      tone="neutral"
      busy={sessions.busy}
      onClose={onClose}
      onSubmit={() => { void remove(); }}
      initialFocusRef={cancelRef}
      footer={(
        <div className="flex flex-col gap-3.5">
          <SheetError error={error} />
          <SheetFooter even>
            <SheetButton ref={cancelRef} kind="cancel" onClick={onClose}>Cancel</SheetButton>
            <SheetButton kind="danger" disabled={sessions.busy}>Delete</SheetButton>
          </SheetFooter>
        </div>
      )}
    >
      <div
        className="flex flex-col gap-1 border px-3.5 py-3"
        style={{ borderRadius: 12, background: 'var(--deep-card-bg)', borderColor: 'var(--deep-card-border)' }}
      >
        <div dir="auto" className="text-[13px] font-extrabold" style={{ color: 'var(--t-text)' }}>
          <bdi>{sessionLabel(session)}</bdi>
        </div>
        <div className="hp-mono text-[11.5px] font-bold" style={{ color: 'var(--t-text-3)' }}>{meta}</div>
      </div>
      <p className="m-0 text-[12.5px] font-semibold" style={{ color: 'var(--t-text-2)' }}>
        {links > 0
          ? `The ${links === 1 ? '' : `${links} `}video link${links === 1 ? ' is' : 's are'} removed from the group. The videos themselves stay where they are (YouTube, Google Photos).`
          : 'Nothing else is removed — this training has no times and no links.'}
      </p>
    </DeepSheet>
  );
}

export default DeleteTrainingSheet;
