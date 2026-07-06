// Категории единой страницы результатов (results_main.html?category=<key>).
// Ключи URL-контракта из design_handoff_category_selector: all | young8_11 | junior | masters.
// Хардкод (без похода в /api/categories): категория задаёт табы селектора,
// заголовок страницы и темы оформления.
export type ResultsCategory = {
  key: string;
  /** Лейбл таба в селекторе соревнований. */
  label: string;
  /** Заголовок страницы (document.title). */
  title: string;
  competitionTheme: string;
  trainingTheme?: string;
};

export const RESULTS_CATEGORIES: ResultsCategory[] = [
  {
    key: 'all',
    label: 'All',
    title: 'Results',
    competitionTheme: 'competition-emerald',
  },
  {
    key: 'young8_11',
    label: 'Young 8–11',
    title: 'Youth Results',
    competitionTheme: 'competition-emerald',
  },
  {
    key: 'junior',
    label: 'Junior',
    title: 'Junior Results',
    competitionTheme: 'competition-emerald',
  },
  {
    key: 'masters',
    label: 'Masters',
    title: 'Masters Results',
    competitionTheme: 'competition-warm',
    trainingTheme: 'training-dashboard',
  },
];

export const DEFAULT_CATEGORY_KEY = 'all';

// Старые ключи (?cat=results-main и т.п.) → новые категории; ссылки могли остаться в закладках.
const LEGACY_CATEGORY_ALIASES: Record<string, string> = {
  'results-main': 'junior',
  'results-youth-team': 'young8_11',
  'results-masters': 'masters',
  dolphin: 'masters',
};

export function resolveCategoryKey(raw: string | null): string {
  if (!raw) return DEFAULT_CATEGORY_KEY;
  const key = LEGACY_CATEGORY_ALIASES[raw] ?? raw;
  return RESULTS_CATEGORIES.some((c) => c.key === key) ? key : DEFAULT_CATEGORY_KEY;
}

// Применяет категорию до первого рендера: useTheme читает data-атрибуты <body> при монтировании.
export function applyResultsCategory(rawKey: string | null): ResultsCategory {
  const key = resolveCategoryKey(rawKey);
  const cat = RESULTS_CATEGORIES.find((c) => c.key === key)!;

  document.title = cat.title;
  document.body.setAttribute('data-competition-theme', cat.competitionTheme);
  if (cat.trainingTheme) {
    document.body.setAttribute('data-training-theme', cat.trainingTheme);
  } else {
    document.body.removeAttribute('data-training-theme');
  }
  return cat;
}
