// Overview 2 — реестр модулей мозаики (9d/9f). Порядок = порядок плиток.
// Цвет модуля задаётся ТОЛЬКО css-классом ov2-module--<key> (см. overview2.css),
// компоненты цветов не знают.

export type ModuleKey = 'clubs' | 'records' | 'champions' | 'hpa' | 'media' | 'favorites';

export const MODULE_ORDER: ModuleKey[] = ['clubs', 'records', 'champions', 'hpa', 'media', 'favorites'];

export const MODULE_LABEL: Record<ModuleKey, string> = {
  clubs: 'Top clubs',
  records: 'New records',
  champions: 'Champions',
  hpa: 'High Point',
  media: 'Media',
  favorites: 'Favorites',
};

export function isModuleKey(v: string | null): v is ModuleKey {
  return v != null && (MODULE_ORDER as string[]).includes(v);
}

/** Инициалы клуба/пловца для круглого плейсхолдера лого (первые буквы двух слов). */
export function initials(name: string): string {
  const words = name.trim().split(/\s+/).filter(Boolean);
  if (words.length === 0) return '?';
  if (words.length === 1) return words[0].slice(0, 2);
  return words[0][0] + words[1][0];
}
