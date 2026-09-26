import React, { useEffect, useRef, useState } from 'react';
import ConfirmDialog from '../../components/confirm-dialog/confirm-dialog';
import { useGroupDeleteImpact } from '../use-my-hub-groups';
import type { HubGroupDeleteImpact, SaveResult } from '../my-groups-types';

/**
 * Подтверждение удаления группы — панель «My groups».
 *
 * Удаление жёсткое: каскадом уходят состав, тренировки с результатами, медиа группы и
 * публикации участников. Раньше это был голый `window.confirm` с именем группы — ни слова о
 * том, что пропадёт, и один рефлекторный клик до потери тренировок. Поэтому:
 *
 * 1. **Перечень потерь с сервера** (`delete-impact`): только ненулевое, с оговорками, что
 *    остаётся (сами пловцы — на сайте, личные медиа — у авторов в My media).
 * 2. **Непустую группу подтверждают вводом имени.** Годится и английское имя: иврит на
 *    английской раскладке набирать неудобно, а цель — остановить рефлекс, не экзамен.
 *    Пустую (только что созданную) удаляют одной кнопкой — там терять нечего.
 *
 * Оболочка — общий `ConfirmDialog` (портал, тема, Esc, ошибка, «Deleting…»); здесь только
 * то, что своё у удаления группы: перечень и ввод имени.
 */
export default function DeleteGroupDialog({ groupId, groupName, onConfirm, onClose }: {
  groupId: number;
  /** Имя из строки списка — для шапки, пока перечень грузится. */
  groupName: string;
  /** Само удаление. При успехе диалог закрывает вызывающий, здесь показываем только ошибку. */
  onConfirm: () => Promise<SaveResult>;
  onClose: () => void;
}) {
  const { impact, error: loadError } = useGroupDeleteImpact(groupId);
  const [typed, setTyped] = useState('');
  const inputRef = useRef<HTMLInputElement>(null);

  useEffect(() => { if (impact?.hasContent) inputRef.current?.focus(); }, [impact]);

  const needsTyping = impact?.hasContent ?? false;
  const canDelete = impact != null && (!needsTyping || matchesName(typed, impact));

  return (
    <ConfirmDialog
      // Имя — в <bdi>: ивритское имя внутри английской фразы иначе утаскивает соседние
      // кавычки в свой RTL-прогон, и они встают задом наперёд.
      title={<>Delete “<bdi>{impact?.name ?? groupName}</bdi>”?</>}
      confirmLabel="Delete group"
      busyLabel="Deleting…"
      confirmDisabled={!canDelete}
      onConfirm={async () => {
        const result = await onConfirm();
        return { success: result.success, error: result.error ?? 'Could not delete the group' };
      }}
      onClose={onClose}
    >
      {loadError && (
        <p className="mt-3 text-[13px] font-bold text-[var(--t-danger)]" role="alert">{loadError}</p>
      )}
      {!impact && !loadError && (
        <p className="mt-3 text-[13px] text-[var(--t-text-2)]">Checking what will be deleted…</p>
      )}

      {impact && (
        <>
          <ImpactList impact={impact} />
          <p className="mt-3 text-[12.5px] font-bold text-[var(--t-danger)]">This cannot be undone.</p>

          {needsTyping && (
            <label className="mt-4 block">
              <span className="mb-1.5 block text-[12px] text-[var(--t-text-2)]">
                Type the group name to confirm: <b className="text-[var(--t-text)]"><bdi>{impact.name}</bdi></b>
                {impact.nameEn && <> or <b className="text-[var(--t-text)]"><bdi>{impact.nameEn}</bdi></b></>}
              </span>
              {/* Enter в поле подтверждает сам — поле внутри формы ConfirmDialog. */}
              <input
                ref={inputRef}
                value={typed}
                onChange={(e) => setTyped(e.target.value)}
                dir="auto"
                autoComplete="off"
                spellCheck={false}
                aria-label="Group name"
                className="w-full rounded-[10px] border border-[var(--t-accent-border)] bg-[var(--t-input-bg)] px-3 py-2 text-[13px] text-[var(--t-text)] outline-none focus:border-[var(--t-accent)]"
              />
            </label>
          )}
        </>
      )}
    </ConfirmDialog>
  );
}

/** Только ненулевое — список «0 trainings, 0 media» читается как шум и прячет важное. */
function ImpactList({ impact: i }: { impact: HubGroupDeleteImpact }) {
  const lines: string[] = [];
  if (i.swimmers) lines.push(`${count(i.swimmers, 'swimmer')} on the roster — the swimmers themselves stay on the site`);
  if (i.accountMembers) lines.push(count(i.accountMembers, 'account member'));
  if (i.admins) lines.push(count(i.admins, 'group admin'));
  if (i.trainingSessions) lines.push(`${count(i.trainingSessions, 'training session')} with ${count(i.trainingResults, 'result')}`);
  if (i.media) lines.push(`${count(i.media, 'media item')} from the gallery and trainings`);
  if (i.mediaPublications) {
    lines.push(`${count(i.mediaPublications, 'media post')} shared to the group — the originals stay in their authors’ My media`);
  }
  if (i.leveledSwimmers) lines.push(`Levels of ${count(i.leveledSwimmers, 'swimmer')}`);
  if (i.lanePlans) lines.push(`${count(i.lanePlans, 'lane plan')} with workouts and who swims where`);
  if (i.isOfficial) lines.push(`Official group status of ${i.clubName ?? 'the club'}`);
  if (i.hasPendingClubRequest) lines.push('A pending request for official status');

  if (lines.length === 0) {
    return <p className="mt-3 text-[13px] text-[var(--t-text-2)]">The group is empty — nothing else will be deleted.</p>;
  }
  return (
    <>
      <p className="mt-3 text-[13px] text-[var(--t-text-2)]">Deleting it also removes:</p>
      <ul className="mt-1.5 flex list-disc flex-col gap-1 pl-5 text-[13px]">
        {lines.map((line) => <li key={line}>{line}</li>)}
      </ul>
    </>
  );
}

function count(n: number, noun: string): string {
  return `${n} ${n === 1 ? noun : `${noun}s`}`;
}

/** Без регистра и крайних пробелов; английское имя годится наравне с основным. */
function matchesName(typed: string, impact: HubGroupDeleteImpact): boolean {
  const t = typed.trim().toLocaleLowerCase();
  if (!t) return false;
  return [impact.name, impact.nameEn].some((n) => n != null && n.trim().toLocaleLowerCase() === t);
}
