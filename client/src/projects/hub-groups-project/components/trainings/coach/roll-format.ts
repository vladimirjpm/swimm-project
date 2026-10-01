import type React from 'react';
import type {
  HubGroupBreak, RsvpAnswer, RsvpNote, TrainingLane, TrainingLaneView, TrainingRsvpPerson,
} from '../../../types';

/**
 * Чистые функции переклички тренера (Т6, docs/tasks/trainings-tab-t6-sonnet.md; хендофф Trainings
 * Tab, 2a / 3b): в какой группе человек, порядок строк, подписи статуса, мета-строка, цвета
 * кнопки статуса. Без JSX и без запросов — `roll-call.tsx` и шторка человека только рисуют.
 */

/** Группа человека в перекличке: ответ, иначе «не тренируется» / «на перерыве» / «нет ответа». */
export type RollGroup = RsvpAnswer | 'none' | 'break' | 'not_training';

/** Порядок строк: сначала те, кого надо спросить, дальше ответившие, в конце — вне зачёта. */
export const ROLL_ORDER: RollGroup[] = ['none', 'yes', 'maybe', 'no', 'break', 'not_training'];

/**
 * Группа человека. `not_training` и `break` — только БЕЗ ответа: ответивший остаётся в своей
 * группе ответа (статус при этом виден в мете строки).
 */
export function groupOf(p: TrainingRsvpPerson): RollGroup {
  if (p.answer) return p.answer;
  if (p.not_training) return 'not_training';
  if (p.on_break) return 'break';
  return 'none';
}

/** Порядок строк `ROLL_ORDER`; внутри группы — как пришло с сервера (сортировка устойчивая). */
export function sortRoll(people: TrainingRsvpPerson[]): TrainingRsvpPerson[] {
  const rank = (p: TrainingRsvpPerson) => ROLL_ORDER.indexOf(groupOf(p));
  return [...people].sort((a, b) => rank(a) - rank(b));
}

/** Цикл кнопки статуса: — → ✓ → ? → ✕ → —; у «⏸ Break» и «Not training» (ответа нет) следующий — ✓. */
export function nextAnswer(p: TrainingRsvpPerson): RsvpAnswer | null {
  if (p.answer === 'yes') return 'maybe';
  if (p.answer === 'maybe') return 'no';
  if (p.answer === 'no') return null;
  return 'yes';
}

/** Текст кнопки статуса. */
export function statusLabel(p: TrainingRsvpPerson): string {
  const g = groupOf(p);
  if (g === 'yes') return '✓ Going';
  if (g === 'maybe') return '? Not sure';
  if (g === 'no') return '✕ Can’t';
  if (g === 'break') return '⏸ Break';
  if (g === 'not_training') return 'Not training';
  return '— Answer';
}

/** Статус словами — для `aria-label` кнопки. */
export function statusSpoken(p: TrainingRsvpPerson): string {
  const g = groupOf(p);
  if (g === 'yes') return 'Going';
  if (g === 'maybe') return 'Not sure';
  if (g === 'no') return 'Can’t come';
  if (g === 'break') return 'On break';
  if (g === 'not_training') return 'Not training';
  return 'No answer';
}

/** Ответ для тоста: «✓ Going» / «— No answer». */
export function answerToastLabel(answer: RsvpAnswer | null): string {
  if (answer === 'yes') return '✓ Going';
  if (answer === 'maybe') return '? Not sure';
  if (answer === 'no') return '✕ Can’t';
  return '— No answer';
}

/**
 * Цвета кнопки статуса и выбранной кнопки сегмента шторки (`PILL` прототипа, на ролях):
 * ✓ — сплошной `--deep-live`, ? — золото, ✕ — красное; без ответа — пунктир, перерыв — сплошная рамка.
 */
export const ANSWER_PILL: Record<RsvpAnswer, React.CSSProperties> = {
  yes: { background: 'var(--deep-live)', color: 'var(--deep-accent-ink)', borderColor: 'var(--deep-live)' },
  maybe: { background: 'var(--deep-gold-chip)', color: 'var(--deep-gold)', borderColor: 'var(--deep-gold-border)' },
  no: { background: 'var(--deep-danger-soft)', color: 'var(--deep-danger)', borderColor: 'var(--deep-danger)' },
};

export const PILL_NONE: React.CSSProperties = {
  background: 'transparent', color: 'var(--t-text-3)', borderColor: 'var(--deep-card-border)', borderStyle: 'dashed',
};

export const PILL_OFF: React.CSSProperties = {
  background: 'transparent', color: 'var(--t-text-3)', borderColor: 'var(--deep-card-border)', borderStyle: 'solid',
};

export function pillStyle(p: TrainingRsvpPerson): React.CSSProperties {
  const g = groupOf(p);
  if (g === 'yes' || g === 'maybe' || g === 'no') return { ...ANSWER_PILL[g], borderStyle: 'solid' };
  return g === 'none' ? PILL_NONE : PILL_OFF;
}

/** Фон аватара по полу привязанного пловца; без пола — приглушённый. */
export function avatarBackground(p: TrainingRsvpPerson): string {
  return p.gender === 'female' ? 'var(--deep-female)' : p.gender === 'male' ? 'var(--deep-male)' : 'var(--deep-text-mute)';
}

/** Подписи заметок в нижнем регистре — для меты строки («late ~10 min»). */
const NOTE_META: Record<RsvpNote, string> = {
  late: 'late ~10 min', 'first-hour': '1st hour only', 'leaving-early': 'leaving early',
};

const MONTH = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];

/** «Oct 12» из `yyyy-MM-dd` — строкой, без часовых поясов. */
export function monthDayLabel(date: string): string {
  const [, m, d] = date.split('-').map(Number);
  return `${MONTH[m - 1] ?? ''} ${d}`;
}

/** «5 days» / «3 months» — длина перерыва в мете «back after …». */
function breakLength(days: number): string {
  if (days < 45) return `${days} ${days === 1 ? 'day' : 'days'}`;
  const months = Math.round(days / 30);
  return `${months} ${months === 1 ? 'month' : 'months'}`;
}

/** Действующий перерыв человека в списке `breaks.data.breaks` (по аккаунту либо пловцу группы). */
export function findBreak(breaks: HubGroupBreak[] | null | undefined, p: TrainingRsvpPerson): HubGroupBreak | null {
  if (!breaks) return null;
  return breaks.find((b) => (p.user_id != null
    ? b.user_id === p.user_id
    : b.user_id == null && b.swimmer_id != null && b.swimmer_id === p.swimmer_id)) ?? null;
}

/**
 * Мета-строка под именем, части через « · »: заметка, «by coach», «no account», перерыв
 * («until Oct 12» — последний день; бессрочно — «on break»), «doesn't train with us»,
 * «back after N days». Пусто — строки нет.
 */
export function personMeta(p: TrainingRsvpPerson, brk: HubGroupBreak | null): string {
  const parts: string[] = [];
  if (p.note) parts.push(NOTE_META[p.note]);
  if (p.set_by_coach) parts.push('by coach');
  if (p.user_id == null) parts.push('no account');
  if (p.on_break) parts.push(brk?.until ? `until ${monthDayLabel(brk.until)}` : 'on break');
  if (p.not_training) parts.push('doesn’t train with us');
  if (p.back_after_days != null) parts.push(`back after ${breakLength(p.back_after_days)}`);
  return parts.join(' · ');
}

/** Дорожка человека в виде по дорожкам занятия (по аккаунту либо пловцу группы). */
export function laneOfPerson(view: TrainingLaneView | null | undefined, p: TrainingRsvpPerson): TrainingLane | null {
  if (!view) return null;
  return view.lanes.find((l) => l.people.some((lp) => (p.user_id != null
    ? lp.user_id === p.user_id
    : lp.user_id == null && lp.swimmer_id != null && lp.swimmer_id === p.swimmer_id))) ?? null;
}

/** «Lane 2 · medium» (уровень — если он есть). */
export function laneCaption(lane: TrainingLane): string {
  return lane.level ? `Lane ${lane.lane_no} · ${lane.level.name.toLowerCase()}` : `Lane ${lane.lane_no}`;
}
