/**
 * Поиск в фильтре клуба (план swimmer-club-change §5, Н7): строка находится по подписи
 * (текущее название) И по прежним названиям клуба — протокол 2024/25 напечатан под
 * «הפועל עירוני הוד השרון», а строка фильтра уже под «אלומה …». Регистр не важен.
 */
export interface ClubMatchable {
  label: string;
  /** Прежние названия клуба (иврит и английские), с сервера — `formerNames` сводки. */
  aliases?: string[];
}

export function matchesClubQuery(option: ClubMatchable, query: string): boolean {
  const q = query.trim().toLowerCase();
  if (!q) return true;
  if (option.label.toLowerCase().includes(q)) return true;
  return (option.aliases ?? []).some((a) => a.toLowerCase().includes(q));
}
