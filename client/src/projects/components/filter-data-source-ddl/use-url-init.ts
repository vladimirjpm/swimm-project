import React from 'react';
import { parseRoute } from '../../../utils/routes';
import {
  resolveCategoryKey,
  ResultsCategory,
} from '../../../utils/constants/results-categories';
import { CompetitionSource } from '../../../utils/helpers/competition-source';

// Компонент может монтироваться несколько раз — грузить данные
// по URL-параметрам должен только один инстанс.
let urlLoadTriggered = false;

interface UrlInitArgs {
  loaded: boolean;
  sources: CompetitionSource[];
  cat: string;
  setCat: (cat: string) => void;
  setSeason: (season: number | 'all' | null) => void;
  setSeasonTouched: (touched: boolean) => void;
  categoriesLoaded: boolean;
  allCategories: ResultsCategory[];
  requestSource: (src: CompetitionSource) => void;
  loadDirect: (sourceParams: Record<string, string>) => void;
}

/**
 * Состояние селектора из адреса — один раз, когда пришёл список соревнований:
 * `?season=`, таб категории (`?category=`, легаси `?cat=`) и источник
 * (`/competitions/{id|last}`, легаси `?competitionId=`, `?eventId=`).
 */
export const useUrlInit = ({
  loaded,
  sources,
  cat,
  setCat,
  setSeason,
  setSeasonTouched,
  categoriesLoaded,
  allCategories,
  requestSource,
  loadDirect,
}: UrlInitArgs) => {
  // Инициализация из URL: ?category= (легаси ?cat=) активирует таб;
  // ?competitionId=<id|last> / ?eventId=<id> грузит соревнование сразу.
  const initializedRef = React.useRef(false);
  React.useEffect(() => {
    if (!loaded || initializedRef.current) return;
    initializedRef.current = true;

    const params = new URLSearchParams(window.location.search);
    const seasonRaw = params.get('season');
    if (seasonRaw === 'all') {
      setSeason('all');
      setSeasonTouched(true);
    } else if (Number(seasonRaw)) {
      setSeason(Number(seasonRaw));
      setSeasonTouched(true);
    }

    const competitionId = parseRoute().competitionId ?? params.get('competitionId');
    const eventId = params.get('eventId');

    let target: CompetitionSource | undefined;
    if (competitionId === 'last') {
      target = sources[0]; // /api/competitions отсортирован по дате desc
    } else if (eventId) {
      target = sources.find((s) => s.kind === 'event' && String(s.id) === eventId);
    } else if (competitionId) {
      target = sources.find((s) => s.kind === 'competition' && String(s.id) === competitionId);
    }

    if (target) {
      setCat(target.category ?? 'all');
      if (!urlLoadTriggered) {
        urlLoadTriggered = true;
        // Ленивый Swims: на старте дефолт — таб Overview, поэтому обычно праймим шапку
        // и откладываем результаты (эффект дотягивания сработает по гейту).
        requestSource(target);
      }
      return;
    }
    // Конкретный id вне списка (например, день события) — грузим напрямую, таб не трогаем.
    if (competitionId || eventId) {
      if (!urlLoadTriggered) {
        urlLoadTriggered = true;
        loadDirect(eventId ? { eventId } : { competitionId: competitionId! });
      }
      return;
    }

    // resolveCategoryKey сводит неизвестные ключи к 'all' — кастомные категории из БД
    // (result-maccabiah и т.п.) пропускаем как есть: их таб появится после /api/categories,
    // а мусорный ключ сбросит валидация ниже.
    const rawCat = params.get('category') ?? params.get('cat');
    const staticKey = resolveCategoryKey(rawCat);
    setCat(staticKey === 'all' && rawCat && rawCat !== 'all' ? rawCat : staticKey);
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [loaded, sources]);

  // Ключ из URL, которого нет ни среди статичных, ни среди кастомных категорий → 'all'.
  React.useEffect(() => {
    if (!categoriesLoaded) return;
    if (cat !== 'all' && !allCategories.some((c) => c.key === cat)) setCat('all');
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [categoriesLoaded, allCategories]);
};
