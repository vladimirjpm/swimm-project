import React from 'react';
import {
  RESULTS_CATEGORIES,
  ResultsCategory,
} from '../../../utils/constants/results-categories';
import CategoryHelper, { CategoryDisplay } from '../../../utils/helpers/category-helper';

/**
 * Табы категорий селектора: статичные канонические (`RESULTS_CATEGORIES`) + кастомные из
 * Admin/Categories, и живые name/badge к ним из `/api/categories`.
 */
export const useCompetitionCategories = () => {
  // name/badge категорий из /api/categories (БД). Ключи: канонические ('kids8_11' и т.п.,
  // маппинг внутри CategoryHelper) + сырые ключи кастомных категорий (result-maccabiah…).
  // Пусто до первой загрузки — тогда используется статичный RESULTS_CATEGORIES.label.
  const [liveCategoryLabels, setLiveCategoryLabels] = React.useState<Record<string, CategoryDisplay>>({});
  // Кастомные категории из Admin/Categories — отдельные табы после статичных.
  const [customCategories, setCustomCategories] = React.useState<ResultsCategory[]>([]);
  const [categoriesLoaded, setCategoriesLoaded] = React.useState(false);

  React.useEffect(() => {
    let cancelled = false;
    Promise.all([CategoryHelper.getCanonicalMap(), CategoryHelper.getAll()]).then(([map, all]) => {
      if (cancelled) return;
      const customs = all
        .filter((c) => !CategoryHelper.SYSTEM_DB_KEYS.has(c.key))
        .sort((a, b) => a.display_order - b.display_order);
      const labels: Record<string, CategoryDisplay> = { ...map };
      customs.forEach((c) => {
        labels[c.key] = { name: c.name, badge: c.badge };
      });
      setLiveCategoryLabels(labels);
      setCustomCategories(
        customs.map((c) => ({
          key: c.key,
          label: c.name,
          title: c.name,
          competitionTheme: 'competition-emerald',
        })),
      );
      setCategoriesLoaded(true);
    });
    return () => {
      cancelled = true;
    };
  }, []);

  // Статичные канонические табы + кастомные категории из БД.
  const allCategories = React.useMemo(
    () => [...RESULTS_CATEGORIES, ...customCategories],
    [customCategories],
  );

  // Живой name+badge из БД (Admin/Categories), пока не загружен — статичный label.
  const categoryLabel = (c: ResultsCategory): string => {
    const live = liveCategoryLabels[c.key];
    if (!live) return c.label;
    return live.badge ? `${live.badge} ${live.name}` : live.name;
  };

  return { liveCategoryLabels, allCategories, categoriesLoaded, categoryLabel };
};
