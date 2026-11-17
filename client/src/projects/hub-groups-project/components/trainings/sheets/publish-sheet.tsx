import React, { useEffect, useState } from 'react';
import { DeepSheet, SheetButton, SheetError, SheetFooter } from '../../../../components/deep/deep-sheet';
import type { LanePlan, LanePlanPublishCheck } from '../../../types';
import GroupLaneBoard from '../../group-lane-board';
import { formatPlanDate, lanePlansApi } from '../../lane-plans-api';
import { useTrainingsTab } from '../trainings-context';
import type { TrainingsSheetProps } from '../trainings-sheets';

/**
 * Диалог публикации плана дорожек (docs/plans/trainings-coach-flow-plan.md, CF3; макет F7; тон
 * `coach`): сводка «сколько человек на скольких дорожках», что мешает опубликовать, галочка
 * «Notify members» (push через Web Push, §8 плана) и превью «What a member sees» — та же доска
 * участника `GroupLaneBoard`, не новая вёрстка.
 *
 * Решение 10: пока есть идущий пловец без уровня не на дорожке, Publish недоступен. Кто это —
 * считает сервер (`GET …/publish-check`: «идущий» — это и ответ аккаунта, привязанного к пловцу,
 * клиент этой привязки не знает); сам Publish проверяет то же. Имя в списке — кнопка: шторка
 * закрывается и открывается карточка человека, где ему задают уровень (CF4); человека в
 * перекличке этого занятия нет — пловец выбран в редакторе (строка «Move … to»).
 *
 * Открывает шторку редактор дорожек ПОСЛЕ того, как дослал несохранённые правки: `plan` — уже
 * сохранённый план, его и увидят участники.
 */

export interface PublishRequest {
  groupId: number;
  /** yyyy-MM-dd — ключ плана. */
  date: string;
  /** Сохранённый план (после автосохранения). */
  plan: LanePlan;
  /** Опубликовать; вернул текст ошибки или null. */
  onPublish: (notify: boolean) => Promise<string | null>;
  /** Перейти к человеку: выбрать пловца в редакторе дорожек. */
  onPerson: (swimmerId: number) => void;
}

type Blocker = LanePlanPublishCheck['blockers'][number];

const blockerName = (b: Blocker) => b.name || b.name_en;

function PublishSheet({ sheet, onClose }: TrainingsSheetProps<'publish'>) {
  const { groupId, date, plan, onPublish, onPerson } = sheet.publish;
  const { selectedRoll, openSheet } = useTrainingsTab();
  const roll = selectedRoll?.rsvp ?? null;
  /** null — проверка ещё идёт. */
  const [blockers, setBlockers] = useState<Blocker[] | null>(null);
  const [notify, setNotify] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    lanePlansApi.publishCheck(groupId, date).then((result) => {
      if (cancelled) return;
      // Проверка не ответила — не блокируем: сервер проверит то же при публикации.
      setBlockers(result.ok ? result.data?.blockers ?? [] : []);
    });
    return () => { cancelled = true; };
  }, [groupId, date]);

  const placed = plan.lanes.reduce((n, l) => n + l.swimmers.length, 0);
  const usedLanes = plan.lanes.filter((l) => l.swimmers.length > 0).length;
  const blocked = blockers != null && blockers.length > 0;

  const publish = async () => {
    if (blockers == null || blocked) return;
    setBusy(true);
    setError(null);
    const err = await onPublish(notify);
    setBusy(false);
    if (err) setError(err);
    else onClose();
  };

  const toPerson = (swimmerId: number) => {
    const person = roll?.date === date
      ? roll.people?.find((p) => (p.linked_swimmer_id ?? p.swimmer_id) === swimmerId) ?? null
      : null;
    if (person) { openSheet({ kind: 'person', person }); return; }
    onClose();
    onPerson(swimmerId);
  };

  const line = 'm-0 text-[12.5px] font-bold';
  return (
    <DeepSheet
      title={`Publish ${formatPlanDate(date)}?`}
      sub="Members see the lanes after you publish"
      tone="coach"
      width={520}
      busy={busy}
      onClose={onClose}
      onSubmit={publish}
      footer={(
        <div className="flex flex-col gap-2.5">
          <SheetError error={error} />
          <SheetFooter>
            <SheetButton kind="cancel" onClick={onClose} disabled={busy}>Keep as draft</SheetButton>
            <SheetButton tone="coach" disabled={busy || blockers == null || blocked}>
              {busy ? 'Publishing…' : blockers == null ? 'Checking…' : 'Publish'}
            </SheetButton>
          </SheetFooter>
        </div>
      )}
    >
      <div className="flex flex-col gap-1.5">
        <p className={line} style={{ color: 'var(--t-text)' }}>
          {placed} {placed === 1 ? 'swimmer' : 'swimmers'} on {usedLanes} {usedLanes === 1 ? 'lane' : 'lanes'}
        </p>
        {plan.unassigned.length > 0 && (
          <p className={line} style={{ color: 'var(--t-text-3)' }}>
            {plan.unassigned.length} unassigned — members see them as “not placed in a lane yet”
          </p>
        )}
      </div>

      {blocked && (
        <div
          className="flex flex-col gap-2 rounded-[12px] border p-3"
          style={{ borderColor: 'var(--deep-gold-border)', background: 'var(--deep-gold-soft)' }}
          role="alert"
        >
          <p className={line} style={{ color: 'var(--deep-gold)' }}>
            {blockers.length === 1 ? 'Going, but has' : 'Going, but have'} no level and no lane. Put them on a
            lane, or set a level in their card, to publish.
          </p>
          <div className="flex flex-wrap gap-1.5">
            {blockers.map((b) => (
              <button
                key={b.swimmer_id}
                type="button"
                onClick={() => toPerson(b.swimmer_id)}
                className="min-h-[36px] cursor-pointer rounded-full border bg-transparent px-3 text-[12px] font-extrabold"
                style={{ borderColor: 'var(--deep-gold-border)', color: 'var(--deep-gold)' }}
                title="Set a level"
              >
                <bdi>{blockerName(b)}</bdi> ›
              </button>
            ))}
          </div>
        </div>
      )}

      <div className="flex flex-col gap-1">
        <label className="flex cursor-pointer items-center gap-2.5">
          <input
            type="checkbox"
            checked={notify}
            onChange={(e) => setNotify(e.target.checked)}
            className="flex-none cursor-pointer"
            style={{ width: 20, height: 20, accentColor: 'var(--deep-ow)' }}
          />
          <span className="text-[12.5px] font-extrabold" style={{ color: 'var(--t-text)' }}>Notify members that lanes are ready</span>
        </label>
        <span className="text-[11px] font-semibold" style={{ color: 'var(--t-text-3)' }}>
          A push to members who turned reminders on. After publishing every change is still saved straight away.
        </span>
      </div>

      <div className="flex flex-col gap-2">
        <div className="text-[10px] font-extrabold uppercase tracking-[.07em]" style={{ color: 'var(--t-text-3)' }}>
          What a member sees
        </div>
        <GroupLaneBoard plan={plan} />
      </div>
    </DeepSheet>
  );
}

export default PublishSheet;
