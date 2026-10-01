import React from 'react';
import { useTrainingsTab, type TrainingsTabState, type TrainingsView } from './trainings-context';
import CoachNextSection from './sections/coach-next-section';
import LanePlanSection from './sections/lane-plan-section';
import MemberNextSection from './sections/member-next-section';
import TrainingFeedSection from './sections/training-feed-section';

/**
 * Состав и порядок секций каждого вида — ДАННЫЕ, а не вёрстка (docs/plans/trainings-tab-redesign-plan.md
 * §3½). Убрать, переставить или подменить блок — правка одной строки здесь; сама секция при
 * этом не меняется. Секция — один файл в `sections/`, данные берёт из `useTrainingsTab()`.
 *
 * `when` решает, показывать ли секцию (роль, ширина, наличие данных); по умолчанию — всегда.
 */

export interface TrainingsSectionDef {
  /** Совпадает с `id` рамки `TrainingsSection` — `data-section` в DOM. */
  id: string;
  when?: (tab: TrainingsTabState) => boolean;
  Component: React.ComponentType;
}

/**
 * Next: ближайшие занятия. Участнику — вид Т5 (`member-next`), управляющему — вид Т6
 * (`coach-next`: навигация по трём занятиям, бассейн со статусом плана, перекличка; вход в Lanes
 * у него — в блоке бассейна). На десктопе — левая колонка.
 */
export const NEXT_SECTIONS: TrainingsSectionDef[] = [
  { id: 'member-next', when: (t) => !t.manages, Component: MemberNextSection },
  { id: 'coach-next', when: (t) => t.manages, Component: CoachNextSection },
];

/** Trainings: лента тренировок (Т4). На десктопе — правая колонка. */
export const TRAININGS_SECTIONS: TrainingsSectionDef[] = [
  { id: 'training-feed', Component: TrainingFeedSection },
];

/** Lanes: план дорожек (Т7). На десктопе — во всю ширину вместо двух колонок. */
export const LANES_SECTIONS: TrainingsSectionDef[] = [
  { id: 'lane-plan', Component: LanePlanSection },
];

export const VIEW_SECTIONS: Record<TrainingsView, TrainingsSectionDef[]> = {
  next: NEXT_SECTIONS,
  trainings: TRAININGS_SECTIONS,
  lanes: LANES_SECTIONS,
};

/** Столбик секций с общим шагом 14px (хендофф: gap 14). */
export function SectionList({ sections }: { sections: TrainingsSectionDef[] }) {
  const tab = useTrainingsTab();
  return (
    <div className="flex min-w-0 flex-col gap-3.5">
      {sections.filter((s) => !s.when || s.when(tab)).map(({ id, Component }) => <Component key={id} />)}
    </div>
  );
}
