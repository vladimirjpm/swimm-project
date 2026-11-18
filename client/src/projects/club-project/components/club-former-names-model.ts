/**
 * Прежние названия клуба (план swimmer-club-change §5, Н6): клуб сменил название, старое
 * осталось в истории — шапка пишет «Formerly: הפועל עירוני הוד השרון (2024–2026)». Сервер
 * отдаёт только переименования (`former_names` в обзоре клуба), последние сверху.
 */
export interface ClubFormerName {
  /** Название как было (иврит) — показывается по умолчанию. */
  name: string;
  /** Английское, если было и отличается. */
  name_en?: string | null;
  /** Год первого и последнего старта под этим названием; null — стартов не было. */
  from_year?: number | null;
  to_year?: number | null;
}

/** «2024–2026», «2025» (один год), null — годов нет. */
export function formerYears(from?: number | null, to?: number | null): string | null {
  if (from != null && to != null) return from === to ? String(from) : `${from}–${to}`;
  if (from != null) return String(from);
  if (to != null) return String(to);
  return null;
}

/** Название для витрины: иврит по умолчанию, английское — фоллбек (правило 28.08.2026). */
export const formerName = (n: ClubFormerName): string => n.name || n.name_en || '';

/** Подпись одного названия: «X (2024–2026)» или просто «X». */
export function formerNameLabel(n: ClubFormerName): string {
  const years = formerYears(n.from_year, n.to_year);
  return years ? `${formerName(n)} (${years})` : formerName(n);
}
