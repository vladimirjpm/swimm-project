import React, { useEffect, useRef } from 'react';
import type { TrainingRsvpState } from '../../use-training-rsvp';
import type { HubGroupDetails } from '../../types';
import { TrainingsTabProvider, useTrainingsTab } from './trainings-context';
import {
  COACH_DESK_SECTIONS, COACH_LIST_SECTIONS, LANES_SECTIONS, MEMBER_PAST_SECTIONS, NEXT_SECTIONS, SectionList,
  TRAININGS_SECTIONS, VIEW_SECTIONS,
} from './trainings-sections';
import TrainingsSegments from './trainings-segments';
import type { TrainingMarks } from './use-training-marks';
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
 * Управляющий (docs/plans/trainings-coach-flow-plan.md, CF1): видов нет — список занятий
 * (`coach-sessions` + лента) и рабочий стол одного занятия (`coach-desk`, `?session=<id>`).
 *
 * Участник (26.11.2026): в Next — список «Past» прошедших с опубликованным планом; Open — вид
 * «только смотреть» того же `?session=<id>` (`member-past-session`): все дорожки, workout всех уровней.
 *
 * Раскладка участника:
 * - телефон — переключатель `Next · Trainings · Lanes` (`?view=`, старое `sessions` → Next);
 * - десктоп (≥960) — без переключателя: две колонки `1.1fr / 1fr` (Next | Trainings), а Lanes —
 *   во всю ширину с «← Back to Next» (6b: редактору дорожек нужна ширина под колонки).
 */
function GroupTrainingsTab({
  group, manages, lanePreview = false, rsvp, onOpenSchedule, onNextTrainingsChanged, onMarksChange,
}: {
  group: HubGroupDetails;
  manages: boolean;
  /**
   * Режим «👁 View» управляющего: вид участника и над ним ряд дорожек — экран пловца выбранной
   * дорожки (`next/lane-preview-picker.tsx`). `manages` при этом false.
   */
  lanePreview?: boolean;
  rsvp?: TrainingRsvpState | null;
  /** «Set schedule» тренера: открыть Admin и прокрутить к карточке расписания (страница группы). */
  onOpenSchedule?: () => void;
  /**
   * Список ближайших занятий изменился (Р9: разовое занятие, отмена / возврат) — страница группы
   * перечитывает `GET …/next-trainings` и подменяет `next_trainings` в `group`.
   */
  onNextTrainingsChanged?: () => void;
  /** Метки видов таба — странице группы для метки на самом табе (см. `use-group-trainings-mark.ts`). */
  onMarksChange?: (marks: TrainingMarks) => void;
}) {
  return (
    <TrainingsTabProvider
      group={group}
      manages={manages}
      lanePreview={lanePreview}
      rsvp={rsvp}
      openSchedule={onOpenSchedule}
      onNextTrainingsChanged={onNextTrainingsChanged}
      onMarksChange={onMarksChange}
    >
      <ScrollToDesk>
        <TrainingsLayout />
      </ScrollToDesk>
      <SheetHost />
    </TrainingsTabProvider>
  );
}

/**
 * Открыли занятие (`?session=`) — вид занятия заменяет весь таб, и он короче списка. Кнопка («Open»
 * в «Past», «View plan» в ленте, 27.11.2026) бывает далеко внизу страницы: без прокрутки зритель
 * оказался бы под видом. Поднимаем к началу таба — только если оно над экраном.
 */
function ScrollToDesk({ children }: { children: React.ReactNode }) {
  const { deskSession } = useTrainingsTab();
  const ref = useRef<HTMLDivElement>(null);
  const id = deskSession?.id ?? null;
  useEffect(() => {
    const el = ref.current;
    if (id && el && el.getBoundingClientRect().top < 0) el.scrollIntoView({ block: 'start' });
  }, [id]);
  return <div ref={ref} style={{ scrollMarginTop: 72 }}>{children}</div>;
}

function TrainingsLayout() {
  const { isDesktop, view, setView, manages, deskSession } = useTrainingsTab();

  // Тренер: список занятий → рабочий стол занятия (CF1), на обеих ширинах; видов у него нет.
  if (manages) return <SectionList sections={deskSession ? COACH_DESK_SECTIONS : COACH_LIST_SECTIONS} />;

  // Участник открыл прошедшее из «Past» (26.11.2026) — вид «только смотреть», без сегментов и колонок.
  if (deskSession) return <SectionList sections={MEMBER_PAST_SECTIONS} />;

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
