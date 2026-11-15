import React from 'react';
import type { TrainingRsvpPerson, TrainingSession } from '../../types';
import AddVideoSheet from './sheets/add-video-sheet';
import BreakSheet from './sheets/break-sheet';
import DeleteTrainingSheet from './sheets/delete-training-sheet';
import PersonSheet from './sheets/person-sheet';
import PoolSetupSheet, { type PoolSetup } from './sheets/pool-setup-sheet';
import PublishSheet, { type PublishRequest } from './sheets/publish-sheet';
import TrainingFormSheet from './sheets/training-form-sheet';
import TrainingLightbox from './sheets/training-lightbox';
import WhoIsThisSheet, { type WhoIsThisRequest } from './sheets/who-is-this-sheet';

/**
 * Реестр форм таба Trainings (docs/plans/trainings-tab-redesign-plan.md §3½). Формы — шторки
 * снизу на телефоне и модалы 480 по центру на десктопе, НИКОГДА не в списке (хендофф §Forms).
 *
 * Секция форму не рисует и не знает, как она выглядит: она только просит
 * `openSheet({ kind: 'add-video', sessionId })`. Сами формы живут здесь, в `SHEETS`, — так
 * переделка формы не трогает секцию, а секцию можно заменить, не теряя формы.
 *
 * Т3: оболочка — общий `DeepSheet` (`components/deep/deep-sheet.tsx`), формы — в `sheets/`.
 * Т6 добавил `person` (шторка человека переклички тренера), CF2 потока тренера — `pool-setup`, CF3 — `publish`, CF4 — `who-is-this`. Вид без записи в реестре хост
 * ничего не рисует — `hasSheet` говорит, есть ли форма.
 */

export type TrainingsSheet =
  | { kind: 'new-training' }
  | { kind: 'edit-training'; session: TrainingSession }
  | { kind: 'delete-training'; session: TrainingSession }
  /** Тренировка заранее выбрана, если форму открыли из её карточки. */
  | { kind: 'add-video'; sessionId?: number }
  | { kind: 'break' }
  /** Человек переклички (⋮ / долгое нажатие у тренера): статус, ответ, заметка. */
  | { kind: 'person'; person: TrainingRsvpPerson }
  /** Настройка бассейна (CF2): открывает редактор дорожек со снимком своего черновика. */
  | { kind: 'pool-setup'; setup: PoolSetup }
  /** Диалог публикации плана (CF3): открывает редактор дорожек с уже сохранённым планом. */
  | { kind: 'publish'; publish: PublishRequest }
  /** «Who is this?» (CF4): кем аккаунт стоит в группе — заявка или аккаунт без связи с пловцом. */
  | { kind: 'who-is-this'; who: WhoIsThisRequest }
  | { kind: 'lightbox'; sessionId: number; index: number };

export type TrainingsSheetKind = TrainingsSheet['kind'];

export interface TrainingsSheetProps<K extends TrainingsSheetKind = TrainingsSheetKind> {
  sheet: Extract<TrainingsSheet, { kind: K }>;
  onClose: () => void;
}

type SheetRegistry = { [K in TrainingsSheetKind]?: React.ComponentType<TrainingsSheetProps<K>> };

const SHEETS: SheetRegistry = {
  'new-training': TrainingFormSheet,
  'edit-training': TrainingFormSheet,
  'add-video': AddVideoSheet,
  'delete-training': DeleteTrainingSheet,
  break: BreakSheet,
  person: PersonSheet,
  'pool-setup': PoolSetupSheet,
  publish: PublishSheet,
  'who-is-this': WhoIsThisSheet,
  lightbox: TrainingLightbox,
};

/** Есть ли уже форма этого вида — пока нет, секция оставляет свою старую (инлайн) форму. */
export function hasSheet(kind: TrainingsSheetKind): boolean {
  return SHEETS[kind] != null;
}

/** Рисует открытую форму; одна на таб. Ставится один раз — в корне таба. */
export function TrainingsSheetHost({ sheet, onClose }: { sheet: TrainingsSheet | null; onClose: () => void }) {
  if (!sheet) return null;
  const Sheet = SHEETS[sheet.kind] as React.ComponentType<TrainingsSheetProps> | undefined;
  return Sheet ? <Sheet sheet={sheet} onClose={onClose} /> : null;
}
