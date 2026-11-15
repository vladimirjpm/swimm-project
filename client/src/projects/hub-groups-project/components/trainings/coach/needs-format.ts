import type { TrainingNeeds } from '../../../types';

/**
 * «Needs you» рабочего стола тренера (docs/plans/trainings-coach-flow-plan.md, CF4) — чистые функции:
 * сколько строк у полосы. Строки считает сервер (`TrainingRsvp.needs`): идущие без уровня, дорожки
 * сверх лимита, заявки на вступление, аккаунты без связи с пловцом. Своего списка «проблем» нет.
 */

export function needsCount(needs: TrainingNeeds | null | undefined): number {
  if (!needs) return 0;
  return needs.no_level.length + needs.over_lanes.length + needs.requests.length + needs.unlinked.length;
}

/** «1 needs you» / «3 need you» — метка карточки занятия и таба. */
export function needsLabel(count: number): string {
  return `${count} ${count === 1 ? 'needs' : 'need'} you`;
}
