// Категории единой страницы результатов (results_main.html?category=<key>).
// Ключи URL-контракта: all | kids8_11 | young11_14 | juniors | adults | masters.
// Возрастная лестница (2026-07-31): Kids 8–11 (ילדים) → Young 11–14 (צעירים) →
// Juniors (נוער) → Adults (בוגרים) → Masters. Ключи табов соответствуют подписям;
// старые ключи из закладок уводит LEGACY_CATEGORY_ALIASES ниже.
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
    competitionTheme: 'competition-blue',
  },
  {
    // ילדים
    key: 'kids8_11',
    label: 'Kids',
    title: 'Kids Results',
    competitionTheme: 'competition-blue',
  },
  {
    // צעירים
    key: 'young11_14',
    label: 'Young',
    title: 'Young Results',
    competitionTheme: 'competition-blue',
  },
  {
    // נוער
    key: 'juniors',
    label: 'Juniors',
    title: 'Juniors Results',
    competitionTheme: 'competition-blue',
  },
  {
    // בוגרים (взрослые).
    key: 'adults',
    label: 'Adults',
    title: 'Adults Results',
    competitionTheme: 'competition-blue',
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

// Старые ключи из закладок → новые табы. Два слоя истории:
//  1. ключи табов до 2026-07-31: young8_11 (Kids 8–11), junior (11–14 — теперь «Young»);
//  2. ещё более старые ссылки с Category.Key в URL (?cat=results-main и т.п.). Их читаем
//     по ЗНАЧЕНИЮ НА МОМЕНТ ССЫЛКИ: тогда results-youth-team = Kids, results-junior-results
//     = 11–14, results-main = בוגרים. После ротации ключей 2026-07-31 те же строки в БД
//     означают другое — поэтому это таблица истории, а не отражение текущих Category.Key.
const LEGACY_CATEGORY_ALIASES: Record<string, string> = {
  young8_11: 'kids8_11',
  junior: 'young11_14',
  'results-main': 'adults',
  'results-youth-team': 'kids8_11',
  'results-junior-results': 'young11_14',
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
