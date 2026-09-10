import React, { useEffect, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { useDeepThemeClass } from '../../components/deep/use-deep-theme-class';
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
 * Не общий `Popup` — по той же причине, что пикер стартового протокола (docs/ui-components.md
 * §6): тот синглтон на Redux с закрытым перечнем типов. Портал в `body`, поэтому класс темы
 * вешаем на корень сами — иначе роли `--t-*` снаружи страницы пустые.
 */
export default function DeleteGroupDialog({ groupId, groupName, onConfirm, onClose }: {
  groupId: number;
  /** Имя из строки списка — для шапки, пока перечень грузится. */
  groupName: string;
  /** Само удаление. При успехе диалог закрывает вызывающий, здесь показываем только ошибку. */
  onConfirm: () => Promise<SaveResult>;
  onClose: () => void;
}) {
  const deepThemeClass = useDeepThemeClass();
  const { impact, error: loadError } = useGroupDeleteImpact(groupId);
  const [typed, setTyped] = useState('');
  const [deleting, setDeleting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const inputRef = useRef<HTMLInputElement>(null);

  // Esc закрывает, скролл страницы под диалогом гасим — как у пикера стартового протокола.
  // Пока идёт удаление, закрыть нельзя: иначе ошибку показать будет некуда.
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape' && !deleting) onClose(); };
    window.addEventListener('keydown', onKey);
    const prev = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    return () => {
      window.removeEventListener('keydown', onKey);
      document.body.style.overflow = prev;
    };
  }, [onClose, deleting]);

  useEffect(() => { if (impact?.hasContent) inputRef.current?.focus(); }, [impact]);

  const needsTyping = impact?.hasContent ?? false;
  const canDelete = impact != null && !deleting && (!needsTyping || matchesName(typed, impact));

  const submit = async () => {
    if (!canDelete) return;
    setDeleting(true);
    setError(null);
    const result = await onConfirm();
    if (!result.success) {
      setDeleting(false);
      setError(result.error ?? 'Could not delete the group');
    }
  };

  return createPortal(
    // z-125 — как у пикера: выше переключателя темы (120), который иначе торчал бы поверх.
    <div
      className={`fixed inset-0 z-[125] flex items-end justify-center sm:items-center ${deepThemeClass}`}
      style={{ background: 'var(--t-scrim)' }}
      onClick={() => { if (!deleting) onClose(); }}
      role="dialog"
      aria-modal="true"
      aria-labelledby="delete-group-title"
    >
      <div
        className="w-full rounded-t-[18px] border border-[var(--t-border)] bg-[var(--t-surface-strong)] p-5 text-[var(--t-text)] shadow-[var(--t-shadow)] sm:w-[min(92vw,480px)] sm:rounded-[18px]"
        // На телефоне диалог прижат к низу — уводим кнопки из-под домашней полосы iOS.
        style={{ paddingBottom: 'calc(20px + env(safe-area-inset-bottom))' }}
        onClick={(e) => e.stopPropagation()}
      >
        {/* Имя — в <bdi>: ивритское имя внутри английской фразы иначе утаскивает соседние
            кавычки в свой RTL-прогон, и они встают задом наперёд. */}
        <h2 id="delete-group-title" className="text-[16px] font-black">
          Delete “<bdi>{impact?.name ?? groupName}</bdi>”?
        </h2>

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
                <input
                  ref={inputRef}
                  value={typed}
                  onChange={(e) => setTyped(e.target.value)}
                  onKeyDown={(e) => { if (e.key === 'Enter') submit(); }}
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

        {error && <p className="mt-3 text-[12.5px] font-bold text-[var(--t-danger)]" role="alert">{error}</p>}

        <div className="mt-5 flex justify-end gap-2">
          <button
            type="button"
            onClick={onClose}
            disabled={deleting}
            className="cursor-pointer rounded-[9px] border border-[var(--t-accent-border)] bg-transparent px-3 py-[7px] text-[12px] font-extrabold text-[var(--t-accent)] transition-colors hover:bg-[var(--t-accent-soft)] disabled:cursor-not-allowed disabled:opacity-40"
          >
            Cancel
          </button>
          <button
            type="button"
            onClick={submit}
            disabled={!canDelete}
            className="cursor-pointer rounded-[9px] border border-[var(--t-danger-border)] bg-[var(--t-danger-soft)] px-3 py-[7px] text-[12px] font-extrabold text-[var(--t-danger)] transition-colors disabled:cursor-not-allowed disabled:opacity-40"
          >
            {deleting ? 'Deleting…' : 'Delete group'}
          </button>
        </div>
      </div>
    </div>,
    document.body,
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
