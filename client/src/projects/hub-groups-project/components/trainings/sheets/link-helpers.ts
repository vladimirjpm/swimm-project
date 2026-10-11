import type { TrainingSessionMedia, TrainingSessionMediaInput } from '../../../types';

/**
 * Общие хелперы форм таба Trainings: ссылки (тип, источник, подпись) и даты. Перенесены из
 * прежней ленты `group-sessions.tsx` (удалена в Т4), чтобы форма ссылки, лайтбокс и форма перерыва не носили
 * по копии. Даты — строки `yyyy-MM-dd` по Израилю; считаем в UTC (`Date.UTC`), иначе часовой
 * пояс машины сдвинет день.
 */

export type LinkKind = TrainingSessionMedia['media_type'];

/** Максимум ссылок одного участника на тренировку — зеркало серверного `MaxMediaPerMember`. */
export const MAX_MEDIA_PER_MEMBER = 10;

/** Тип ссылки по адресу — подсказка формы, человек может поправить. */
export function guessKind(url: string): LinkKind {
  const u = url.trim().toLowerCase();
  if (/photos\.app\.goo\.gl|photos\.google\.com|drive\.google\.com\/drive\/folders|chat\.whatsapp\.com/.test(u)) return 'album';
  if (/\.(jpe?g|png|webp|gif|heic)(\?|#|$)/.test(u)) return 'image';
  return 'video';
}

/** Источник для сервера: у видео — хостинг по адресу, у фото — other, у альбома — album. */
export function sourceOf(kind: LinkKind, url: string): TrainingSessionMediaInput['source_type'] {
  if (kind === 'album') return 'album';
  if (kind === 'image') return 'other';
  const u = url.toLowerCase();
  if (/youtube\.com|youtu\.be/.test(u)) return 'youtube';
  if (/vimeo\.com/.test(u)) return 'vimeo';
  return 'other';
}

/** Подпись источника по адресу: «YouTube» / «Vimeo» / «Google Photos» / «Photo» / «Link». */
export function sourceLabel(url: string, mediaType?: LinkKind): string {
  const u = url.trim().toLowerCase();
  if (/youtube\.com|youtu\.be/.test(u)) return 'YouTube';
  if (/vimeo\.com/.test(u)) return 'Vimeo';
  if (/photos\.app\.goo\.gl|photos\.google\.com/.test(u)) return 'Google Photos';
  if (mediaType === 'image' || /\.(jpe?g|png|webp|gif|heic)(\?|#|$)/.test(u)) return 'Photo';
  return 'Link';
}

const MONTH = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
const WEEKDAY = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];

function parseDate(date: string): [number, number, number] {
  const [y, m, d] = date.split('-').map(Number);
  return [y, m, d];
}

/** «Tue 29 Sep» — подпись даты в формах и лайтбоксе. */
export function formatSheetDate(date: string): string {
  const [y, m, d] = parseDate(date);
  return `${WEEKDAY[new Date(Date.UTC(y, m - 1, d)).getUTCDay()]} ${d} ${MONTH[m - 1]}`;
}

/** `date` + `days` дней (можно отрицательно), `yyyy-MM-dd`. */
export function addDays(date: string, days: number): string {
  const [y, m, d] = parseDate(date);
  return new Date(Date.UTC(y, m - 1, d + days)).toISOString().slice(0, 10);
}

/** Тот же день следующего месяца; в коротком месяце — его последний день (31 янв → 28 фев). */
export function addOneMonth(date: string): string {
  const [y, m, d] = parseDate(date);
  const next = new Date(Date.UTC(y, m, d));
  // Date.UTC переполнился в следующий месяц — берём последний день нужного.
  if (next.getUTCDate() !== d) return new Date(Date.UTC(y, m + 1, 0)).toISOString().slice(0, 10);
  return next.toISOString().slice(0, 10);
}

/** День недели ISO: 1 = Mon … 7 = Sun (как `day` в слотах расписания). */
export function isoWeekday(date: string): number {
  const [y, m, d] = parseDate(date);
  return new Date(Date.UTC(y, m - 1, d)).getUTCDay() || 7;
}
