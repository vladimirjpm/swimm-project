import React from 'react';
import type { TrainingRsvpState } from '../../use-training-rsvp';
import type { HubGroupDetails } from '../../types';
import { TrainingsTabProvider, useTrainingsTab } from './trainings-context';
import { LANES_SECTIONS, NEXT_SECTIONS, SectionList, TRAININGS_SECTIONS, VIEW_SECTIONS } from './trainings-sections';
import TrainingsSegments from './trainings-segments';
import { TrainingsSheetHost } from './trainings-sheets';

/**
 * Таб Trainings группы — каркас редизайна (docs/plans/trainings-tab-redesign-plan.md, Т2;
 * хендофф `!design_handoff/design_handoff_trainings_tab/`, секции 2a / 3a / 3b).
 *
 * Устройство (§3½ плана — модульные секции):
 * - `trainings-context.tsx` — общие данные таба; секции ничего не грузят сами;
 * - `trainings-sections.tsx` — состав и порядок секций каждого вида (данные, а не вёрстка);
 * - `sections/*` — по файлу на блок, рамка у всех одна (`trainings-section.tsx`);
 * - `trainings-sheets.tsx` — реестр форм (шторки / модалы), секция только просит открыть;
 * - `use-training-marks.ts` — метки внимания (считает Т8).
 *
 * Раскладка:
 * - телефон — переключатель `Next · Trainings · Lanes` (`?view=`, старое `sessions` → Next);
 * - десктоп (≥960) — без переключателя: две колонки `1.1fr / 1fr` (Next | Trainings), а Lanes —
 *   во всю ширину с «← Back to Next» (6b: редактору дорожек нужна ширина под колонки).
 */
function GroupTrainingsTab({
  group, manages, rsvp,
}: {
  group: HubGroupDetails;
  manages: boolean;
  rsvp?: TrainingRsvpState | null;
}) {
  return (
    <TrainingsTabProvider group={group} manages={manages} rsvp={rsvp}>
      <TrainingsLayout />
      <SheetHost />
    </TrainingsTabProvider>
  );
}

function TrainingsLayout() {
  const { isDesktop, view, setView } = useTrainingsTab();

  if (!isDesktop) {
    return (
      <div>
        <TrainingsSegments />
        <SectionList sections={VIEW_SECTIONS[view]} />
      </div>
    );
  }

  if (view === 'lanes') {
    return (
      <div className="flex flex-col gap-3">
        <div className="deep-panel-row">
          <button
            type="button"
            onClick={() => setView('next')}
            className="cursor-pointer border-0 bg-transparent p-0 text-[13px] font-extrabold"
            style={{ color: 'var(--t-text-3)' }}
          >
            ← Back to Next
          </button>
        </div>
        <SectionList sections={LANES_SECTIONS} />
      </div>
    );
  }

  return (
    <div className="grid items-start gap-3.5" style={{ gridTemplateColumns: 'minmax(0, 1.1fr) minmax(0, 1fr)' }}>
      <SectionList sections={NEXT_SECTIONS} />
      <SectionList sections={TRAININGS_SECTIONS} />
    </div>
  );
}

function SheetHost() {
  const { sheet, closeSheet } = useTrainingsTab();
  return <TrainingsSheetHost sheet={sheet} onClose={closeSheet} />;
}

export default GroupTrainingsTab;
