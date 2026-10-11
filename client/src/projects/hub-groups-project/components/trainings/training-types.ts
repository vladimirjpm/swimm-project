import type { TrainingSession, TrainingType } from '../../types';

/**
 * Типы тренировки — подписи и значки (решение Влада 01.10.2026: список фиксированный, на весь
 * сайт). Зеркало серверного `TrainingSessionType` (`Swimm.Domain/Entities/TrainingSession.cs`):
 * новый тип добавляется в ОБА места. Название тренировки остаётся свободным текстом — тип только
 * классифицирует: фильтр ленты, значок на карточке, позже — видеоразбор и разовые занятия в Next.
 */

export interface TrainingTypeInfo {
  id: TrainingType;
  label: string;
  /** Значок — текстом; на карточке и в фильтре идёт перед подписью. */
  glyph: string;
}

export const TRAINING_TYPES: TrainingTypeInfo[] = [
  { id: 'swim', label: 'Swim', glyph: '🏊' },
  { id: 'technique', label: 'Technique', glyph: '🎥' },
  { id: 'dryland', label: 'Dryland', glyph: '🏋' },
  { id: 'open-water', label: 'Open water', glyph: '🌊' },
  { id: 'test-set', label: 'Test set', glyph: '🎯' },
  { id: 'other', label: 'Other', glyph: '◦' },
];

/** Обычная тренировка в воде — по умолчанию; на карточке значка не получает (иначе он на каждой). */
export const DEFAULT_TRAINING_TYPE: TrainingType = 'swim';

/** Тип тренировки; нет поля (старый ответ) или незнакомое значение — swim. */
export function trainingTypeOf(session: Pick<TrainingSession, 'type'>): TrainingTypeInfo {
  return TRAINING_TYPES.find((t) => t.id === session.type) ?? TRAINING_TYPES[0];
}
