/**
 * Метки внимания таба Trainings (`marks-spec.md` хендоффа, docs/plans/trainings-tab-redesign-plan.md).
 * Одна метка на место, приоритет красная › золотая › акцентная, зелёных нет:
 * - красная — нет ответа на ближайшее занятие (у тренера — число не ответивших);
 * - золотая — план дорожек черновик / есть неразложенные (тренер), тебя перенесли (участник);
 * - акцентная — новые видео в ленте.
 *
 * ⚠ Т2 — только контракт: каркас уже рисует метки на сегментах и в заголовках секций, а
 * считать их будет этап Т8 (данные — `TrainingRsvp`, `LanePlan.my_swimmers[].moved_from_lane`,
 * `TrainingSessionList.new_media_count`). Пока все места — null, меток нет.
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

/** Т8 заменит тело расчётом по `marks-spec.md`; сигнатура — контракт для мест показа. */
export function useTrainingMarks(): TrainingMarks {
  return NO_MARKS;
}

/** Цвет точки метки — роль темы, не хекс. */
export const MARK_COLOR: Record<TrainingMarkTone, string> = {
  red: 'var(--deep-danger)',
  gold: 'var(--deep-gold-bar)',
  accent: 'var(--deep-accent)',
};
