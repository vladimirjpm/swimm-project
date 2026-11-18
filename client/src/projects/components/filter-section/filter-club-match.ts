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

/**
 * Подпись и синонимы строки фильтра клуба (Р97). Значение фильтра — название КАК НАПЕЧАТАНО
 * в протоколе (по нему фильтруются строки), а подпись — ТЕКУЩЕЕ название клуба: фильтры, как
 * и страница клуба, говорят текущим (решение Влада 18.11.2026). Напечатанное и прежние
 * названия уходят в синонимы поиска.
 */
export function clubFilterLabel(
  printed: string,
  now: string | null | undefined,
  formerNames?: string[] | null,
): { label: string; aliases?: string[] } {
  const current = now?.trim();
  const aliases = [...(current && current !== printed ? [printed] : []), ...(formerNames ?? [])]
    .filter((a, i, all) => a && all.indexOf(a) === i && a !== (current || printed));
  return {
    label: current || printed,
    aliases: aliases.length > 0 ? aliases : undefined,
  };
}
