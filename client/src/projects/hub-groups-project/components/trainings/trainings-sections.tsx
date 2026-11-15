import React from 'react';
import { useTrainingsTab, type TrainingsTabState, type TrainingsView } from './trainings-context';
import CoachDeskSection from './sections/coach-desk-section';
import CoachSessionsSection from './sections/coach-sessions-section';
import LanePlanSection from './sections/lane-plan-section';
import MemberNextSection from './sections/member-next-section';
import TrainingFeedSection from './sections/training-feed-section';

/**
 * Состав и порядок секций каждого вида — ДАННЫЕ, а не вёрстка (docs/plans/trainings-tab-redesign-plan.md
 * §3½). Убрать, переставить или подменить блок — правка одной строки здесь; сама секция при
 * этом не меняется. Секция — один файл в `sections/`, данные берёт из `useTrainingsTab()`.
 *
 * `when` решает, показывать ли секцию (роль, ширина, наличие данных); по умолчанию — всегда.
 *
 * Виды `Next · Trainings · Lanes` — участнику. У управляющего их нет
 * (docs/plans/trainings-coach-flow-plan.md, CF1): список занятий (`COACH_LIST_SECTIONS`) и рабочий
 * стол одного занятия (`COACH_DESK_SECTIONS`).
 */

export interface TrainingsSectionDef {
  /** Совпадает с `id` рамки `TrainingsSection` — `data-section` в DOM. */
  id: string;
  when?: (tab: TrainingsTabState) => boolean;
  Component: React.ComponentType;
}

/** Next участника: ближайшие занятия (Т5). На десктопе — левая колонка. */
export const NEXT_SECTIONS: TrainingsSectionDef[] = [
  { id: 'member-next', Component: MemberNextSection },
];

/** Trainings: лента тренировок (Т4). На десктопе — правая колонка. */
export const TRAININGS_SECTIONS: TrainingsSectionDef[] = [
  { id: 'training-feed', Component: TrainingFeedSection },
];

/** Lanes участника: опубликованный план дорожек (Т7). На десктопе — во всю ширину вместо двух колонок. */
export const LANES_SECTIONS: TrainingsSectionDef[] = [
  { id: 'lane-plan', Component: LanePlanSection },
];

export const VIEW_SECTIONS: Record<TrainingsView, TrainingsSectionDef[]> = {
  next: NEXT_SECTIONS,
  trainings: TRAININGS_SECTIONS,
  lanes: LANES_SECTIONS,
};

/** Тренер, вход в таб: ближайшие занятия карточками, ниже лента прошедших (CF1, макет F1). */
export const COACH_LIST_SECTIONS: TrainingsSectionDef[] = [
  { id: 'coach-sessions', Component: CoachSessionsSection },
  { id: 'training-feed', Component: TrainingFeedSection },
];

/** Тренер, рабочий стол занятия: перекличка и план дорожек рядом (CF1, макет F2). */
export const COACH_DESK_SECTIONS: TrainingsSectionDef[] = [
  { id: 'coach-desk', Component: CoachDeskSection },
];

/** Столбик секций с общим шагом 14px (хендофф: gap 14). */
export function SectionList({ sections }: { sections: TrainingsSectionDef[] }) {
  const tab = useTrainingsTab();
  return (
    <div className="flex min-w-0 flex-col gap-3.5">
      {sections.filter((s) => !s.when || s.when(tab)).map(({ id, Component }) => <Component key={id} />)}
    </div>
  );
}
