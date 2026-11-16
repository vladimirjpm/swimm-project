/**
 * Строки подписи «Records — World Aquatics masters: checked today, updated 16 Sep 2026» —
 * чистые функции без импортов (их гоняет `node --test`, records-checked-model.test.mjs).
 *
 * Две даты у источника (решение Влада 16.11.2026):
 * • **checked** — последняя сверка, после которой НАШИ данные совпадают с источником: без
 *   изменений или с изменениями, которые потом применили. Сверка, нашедшая неприменённые
 *   изменения, её не сдвигает (сервер отдаёт уже такую дату — `VerifiedAt`);
 * • **updated** — когда справочник этого источника у нас реально менялся.
 *
 * Источник без обеих дат не подписывается вовсе.
 */

export interface FreshnessRow {
  source: string;
  checkedAt: string | null;
  changedAt: string | null;
}

export interface CheckedLine {
  source: string;
  label: string;
  checkedAt: string | null;
  changedAt: string | null;
}

const DAY_MS = 86400000;

const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];

/**
 * «16 Sep 2026» — своим форматом, а не `toLocaleDateString('en-GB')`: та в свежих ICU пишет
 * «Sept», в старых — «Sep», и одна и та же дата выглядела бы по-разному в разных браузерах.
 * День — по местному времени, как и у относительной даты.
 */
export function shortDate(iso: string): string {
  const d = new Date(iso);
  return `${d.getDate()} ${MONTHS[d.getMonth()]} ${d.getFullYear()}`;
}

/** «today» / «yesterday» / «3 days ago»; старше месяца — дата. */
export function checkedAgo(iso: string, now: number = Date.now()): string {
  const days = Math.floor((now - new Date(iso).getTime()) / DAY_MS);
  if (days <= 0) return 'today';
  if (days === 1) return 'yesterday';
  if (days < 30) return `${days} days ago`;
  return shortDate(iso);
}

/** Строки подписи в порядке `sources`: только источники, у которых есть хоть одна дата. */
export function checkedLines(
  freshness: FreshnessRow[],
  sources: string[],
  labels: Record<string, string>,
): CheckedLine[] {
  return sources.flatMap((source) => {
    const f = freshness.find((x) => x.source === source);
    if (!f || (!f.checkedAt && !f.changedAt)) return [];
    return [{ source, label: labels[source] ?? source, checkedAt: f.checkedAt, changedAt: f.changedAt }];
  });
}

/** «checked today, updated 16 Sep 2026» / «updated 16 Sep 2026» / «checked today». */
export function lineText(line: CheckedLine, now: number = Date.now()): string {
  const parts: string[] = [];
  if (line.checkedAt) parts.push(`checked ${checkedAgo(line.checkedAt, now)}`);
  if (line.changedAt) parts.push(`updated ${shortDate(line.changedAt)}`);
  return parts.join(', ');
}

/** Полные даты для подсказки. */
export function lineTitle(line: CheckedLine): string {
  const parts: string[] = [];
  if (line.checkedAt)
    parts.push(`Last verified against ${line.label}: ${new Date(line.checkedAt).toLocaleString('en-GB')}`);
  if (line.changedAt)
    parts.push(`Records from ${line.label} last changed: ${new Date(line.changedAt).toLocaleString('en-GB')}`);
  return parts.join('\n');
}
