import { useMemo } from 'react';
import type { LanePlan, TrainingRsvp, TrainingSessionList } from '../../types';

/**
 * Метки внимания таба Trainings (`marks-spec.md` хендоффа, docs/plans/trainings-tab-redesign-plan.md).
 * Одна метка на место, зелёных нет:
 * - красная — нет ответа на ближайшее занятие (у тренера — число не ответивших);
 * - золотая — план дорожек черновик / есть неразложенные (тренер), тебя перенесли (участник);
 * - акцентная — новые видео в ленте.
 *
 * Расчёт — чистые функции (`redMark` / `goldMark` / `accentMark`): красную считают и места ВНЕ
 * таба (таб группы, полоса «Next», липкий чип), где контекста таба нет. Хук `useTrainingMarks`
 * собирает из них метки видов и гасит «уже виденное» (флаги ставит контекст таба после `markSeen`).
 */

export type TrainingMarkTone = 'red' | 'gold' | 'accent';

export interface TrainingMark {
  tone: TrainingMarkTone;
  /** Число у тренера («2» не ответивших); нет — просто точка. */
  count?: number;
  /** Текст для читалок и подсказки: «2 haven’t answered», «Draft plan», «3 new videos». */
  label: string;
}

/** Метка на каждый вид таба (сегмент на телефоне, заголовок колонки на десктопе). */
export interface TrainingMarks {
  next: TrainingMark | null;
  trainings: TrainingMark | null;
  lanes: TrainingMark | null;
}

export const NO_MARKS: TrainingMarks = { next: null, trainings: null, lanes: null };

/**
 * Красная: нет ответа на ближайшее занятие. Тренеру — число не ответивших (метка только при
 * числе > 0), участнику — точка. `rsvp` — ответ именно на БЛИЖАЙШЕЕ занятие, не выбранное тренером.
 */
export function redMark(rsvp: TrainingRsvp | null): TrainingMark | null {
  if (!rsvp) return null;
  if (rsvp.can_manage) {
    const count = (rsvp.people ?? []).filter((p) => p.answer == null && !p.on_break && !p.not_training).length;
    return count > 0 ? { tone: 'red', count, label: `${count} haven’t answered` } : null;
  }
  if (rsvp.can_answer && rsvp.mine == null && !rsvp.on_break) {
    return { tone: 'red', label: 'You haven’t answered' };
  }
  return null;
}

/**
 * Золотая: план дорожек на дату ближайшего занятия. Тренеру — черновик и/или неразложенные;
 * участнику — кого-то из «моих» пловцов тренер перенёс на другую дорожку. Всегда точка, без числа.
 */
export function goldMark(plan: LanePlan | null, manages: boolean): TrainingMark | null {
  if (!plan) return null;
  if (manages) {
    const draft = plan.status === 'draft';
    const unassigned = plan.unassigned.length;
    if (!draft && unassigned === 0) return null;
    const label = draft && unassigned > 0 ? `Draft · ${unassigned} unassigned`
      : draft ? 'Draft plan'
        : `${unassigned} unassigned`;
    return { tone: 'gold', label };
  }
  const moved = plan.my_swimmers.filter((s) => s.moved_from_lane != null);
  if (moved.length === 0) return null;
  return { tone: 'gold', label: moved.some((s) => s.kind === 'me') ? 'Coach moved you' : 'Coach moved your swimmer' };
}

/** Акцентная: в ленте есть видео, добавленные другими после последнего захода. */
export function accentMark(list: TrainingSessionList | null): TrainingMark | null {
  const n = list?.new_media_count ?? 0;
  return n > 0 ? { tone: 'accent', label: `${n} new ${n === 1 ? 'video' : 'videos'}` } : null;
}

export interface TrainingMarksInput {
  /** Ответ на ближайшее занятие (`group.next_training`). */
  rsvp: TrainingRsvp | null;
  manages: boolean;
  /** План дорожек на дату ближайшего занятия; нет плана / ещё не пришёл — null. */
  nextPlan: LanePlan | null;
  sessions: TrainingSessionList | null;
  /** Ленту уже показали и `markSeen` вызван — акцентная гаснет локально. */
  trainingsSeen: boolean;
  /** Участник уже открыл Lanes и `markSeen` вызван — золотая участника гаснет локально. */
  lanesSeen: boolean;
}

/** Метки видов таба: Next — красная, Lanes — золотая, Trainings — акцентная. */
export function useTrainingMarks({
  rsvp, manages, nextPlan, sessions, trainingsSeen, lanesSeen,
}: TrainingMarksInput): TrainingMarks {
  return useMemo<TrainingMarks>(() => ({
    next: redMark(rsvp),
    // Тренеру Lanes ничего не гасит: метка держится, пока план черновик / есть неразложенные.
    lanes: !manages && lanesSeen ? null : goldMark(nextPlan, manages),
    trainings: trainingsSeen ? null : accentMark(sessions),
  }), [rsvp, manages, nextPlan, sessions, trainingsSeen, lanesSeen]);
}

/**
 * Одна метка на таб группы «Trainings» (снаружи таба): самая срочная из видов — красная › золотая ›
 * акцентная (`marks-spec.md`, «Precedence per tab»). Нет ни одной — меток нет, зелёных не бывает.
 */
export function tabMark(marks: TrainingMarks): TrainingMark | null {
  return marks.next ?? marks.lanes ?? marks.trainings;
}

/** Цвет точки метки — роль темы, не хекс. */
export const MARK_COLOR: Record<TrainingMarkTone, string> = {
  red: 'var(--deep-danger)',
  gold: 'var(--deep-gold-bar)',
  accent: 'var(--deep-accent)',
};
