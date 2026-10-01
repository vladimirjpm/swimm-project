import React, { useRef, useState } from 'react';
import {
  DeepSheet, SheetButton, SheetError, SheetField, SheetFooter, SHEET_FIELD_CLS, SHEET_MONO_FIELD_CLS, SHEET_TEXTAREA_CLS,
} from '../../../../components/deep/deep-sheet';
import type { TrainingType } from '../../../types';
import { todayInIsrael } from '../../lane-plans-api';
import { useTrainingsTab } from '../trainings-context';
import { DEFAULT_TRAINING_TYPE, TRAINING_TYPES, trainingTypeOf } from '../training-types';
import type { TrainingsSheetProps } from '../trainings-sheets';

/**
 * Форма тренировки (`new-training` / `edit-training`, тон coach): дата, старт, тип, название, заметка.
 * Тип — фиксированный список (`training-types.ts`), по умолчанию Swim; правится и у тренировки с
 * временами (дата и бассейн у неё закрыты, тип — нет).
 * Тренер заводит тренировку и без времён («Underwater filming») — потом цепляет к ней ссылки.
 * У тренировки с временами (`has_results`) дату не поменять — поле закрыто.
 *
 * Запись — через хук списка (`sessions.create` / `update`): он отвечает строкой ошибки или
 * `null`; ошибка встаёт над кнопками, шторка остаётся, успех закрывает её.
 */
// Один компонент на два вида формы: пропсы — структурно (обобщённый `TrainingsSheetProps<A | B>`
// не присваивается записям реестра по каждому виду отдельно).
function TrainingFormSheet({ sheet, onClose }: {
  sheet: TrainingsSheetProps<'new-training'>['sheet'] | TrainingsSheetProps<'edit-training'>['sheet'];
  onClose: () => void;
}) {
  const { sessions } = useTrainingsTab();
  const session = sheet.kind === 'edit-training' ? sheet.session : null;

  const [date, setDate] = useState(session?.date ?? todayInIsrael());
  // У залитой скриптом названия нет — предлагаем её план (сервер держит до 100 символов).
  const [title, setTitle] = useState(session?.title ?? session?.plan?.slice(0, 100) ?? '');
  const [start, setStart] = useState(session?.start ?? '');
  const [note, setNote] = useState(session?.note ?? '');
  const [type, setType] = useState<TrainingType>(session ? trainingTypeOf(session).id : DEFAULT_TRAINING_TYPE);
  const [error, setError] = useState<string | null>(null);
  const nameRef = useRef<HTMLInputElement>(null);

  const dateLocked = !!session?.has_results;
  const canSave = !sessions.busy && !!title.trim() && !!date;

  const save = async () => {
    if (!canSave) return;
    setError(null);
    const input = { date, title: title.trim(), start: start || null, note: note.trim() || null, type };
    const e = session
      ? await sessions.update(session.id, { ...input, pool_type: session.pool_type ?? null })
      : await sessions.create(input);
    if (e) setError(e);
    else onClose();
  };

  return (
    <DeepSheet
      title={session ? 'Edit training' : 'New training'}
      sub={session ? undefined : 'Shows in the trainings list right away'}
      tone="coach"
      busy={sessions.busy}
      onClose={onClose}
      onSubmit={() => { void save(); }}
      initialFocusRef={nameRef}
      footer={(
        <div className="flex flex-col gap-3.5">
          <SheetError error={error} />
          <SheetFooter>
            <SheetButton kind="cancel" onClick={onClose}>Cancel</SheetButton>
            <SheetButton tone="coach" disabled={!canSave}>{session ? 'Save' : 'Create training'}</SheetButton>
          </SheetFooter>
        </div>
      )}
    >
      <div className="grid grid-cols-2 gap-2.5">
        <SheetField label="Date">
          <input
            type="date"
            value={date}
            disabled={dateLocked}
            onChange={(e) => setDate(e.target.value)}
            className={SHEET_MONO_FIELD_CLS}
          />
        </SheetField>
        <SheetField label="Start · optional">
          <input type="time" value={start} onChange={(e) => setStart(e.target.value)} className={SHEET_MONO_FIELD_CLS} />
        </SheetField>
      </div>
      {dateLocked && (
        <p className="m-0 -mt-2 text-[11px] font-bold" style={{ color: 'var(--t-text-3)' }}>
          Date is locked — this training has recorded times.
        </p>
      )}

      <SheetField label="Type" group>
        <div
          className="grid grid-cols-3 gap-[2px] p-[2px]"
          style={{ borderRadius: 12, background: 'var(--deep-seg-track)' }}
          role="group"
          aria-label="Training type"
        >
          {TRAINING_TYPES.map((t) => {
            const on = type === t.id;
            return (
              <button
                key={t.id}
                type="button"
                aria-pressed={on}
                onClick={() => setType(t.id)}
                className="inline-flex h-[36px] cursor-pointer items-center justify-center gap-1 border-0 px-1 text-[12px] font-extrabold"
                style={on
                  ? { background: 'var(--deep-ow)', color: 'var(--deep-accent-ink)', borderRadius: 10 }
                  : { background: 'transparent', color: 'var(--t-text-2)', borderRadius: 10 }}
              >
                <span aria-hidden="true">{t.glyph}</span>
                <span className="truncate">{t.label}</span>
              </button>
            );
          })}
        </div>
      </SheetField>

      <SheetField label="Name">
        <input
          ref={nameRef}
          value={title}
          onChange={(e) => setTitle(e.target.value)}
          placeholder="e.g. Underwater filming"
          maxLength={100}
          dir="auto"
          className={SHEET_FIELD_CLS}
        />
      </SheetField>

      <SheetField label="Note">
        <textarea
          value={note}
          onChange={(e) => setNote(e.target.value)}
          placeholder="Optional"
          maxLength={500}
          dir="auto"
          className={SHEET_TEXTAREA_CLS}
        />
      </SheetField>

      {!session && (
        <p className="m-0 text-[11.5px] font-semibold" style={{ color: 'var(--t-text-3)' }}>
          No times needed — add the training, then attach videos to it.
        </p>
      )}
    </DeepSheet>
  );
}

export default TrainingFormSheet;
