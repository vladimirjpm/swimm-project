import React from 'react';
import { seasonStartYear } from '../../../utils/helpers/season-helper';
import {
  CompetitionSource,
  parseDate,
  monthLabel,
  isChampionshipSource,
} from '../../../utils/helpers/competition-source';

/** Список соревнований селектора — `/api/competitions` (отсортирован по дате desc). */
export const useCompetitionSources = () => {
  const [sources, setSources] = React.useState<CompetitionSource[]>([]);
  const [loaded, setLoaded] = React.useState(false);

  React.useEffect(() => {
    const load = async () => {
      try {
        const resp = await fetch('/api/competitions', { credentials: 'same-origin' });
        setSources(resp.ok ? ((await resp.json()) as CompetitionSource[]) : []);
      } catch (e) {
        console.error('Error loading /api/competitions', e);
        setSources([]);
      }
      setLoaded(true);
    };
    load();
  }, []);

  return { sources, loaded };
};

// Архив: завершённые старше начала прошлого месяца прячутся за «Show N earlier».
const isArchived = (src: CompetitionSource, now: Date): boolean => {
  if (src.status !== 'done') return false;
  const d = parseDate(src.date);
  if (!d) return false;
  const boundary = new Date(now.getFullYear(), now.getMonth() - 1, 1);
  return d < boundary;
};

/**
 * Что из списка видно в панели: состояние фильтров панели (категория, поиск, сезон,
 * «только чемпионаты», раскрытый архив) и производные от него списки — Live/Upcoming,
 * завершённые по месяцам, архив, счётчики табов.
 */
export const useCompetitionList = (sources: CompetitionSource[]) => {
  const [cat, setCat] = React.useState<string>('all');
  const [search, setSearch] = React.useState('');
  const [season, setSeason] = React.useState<number | 'all' | null>(null);
  const [seasonTouched, setSeasonTouched] = React.useState(false);
  const [archiveOpen, setArchiveOpen] = React.useState(false);
  // Тумблер «🏆 Championships» — показывать только чемпионаты Израиля.
  const [champOnly, setChampOnly] = React.useState(false);

  // ── Производные списки ──
  // Сезон, а НЕ календарный год: старт 31/10/2025 принадлежит сезону 2025/26 вместе с
  // февральскими стартами 2026. По getFullYear() он уезжал в «Season 2025» отдельно от
  // своего же сезона (docs/season-boundary-rule.md).
  const seasons = React.useMemo(() => {
    const years = new Set(
      sources
        .map((s) => {
          const d = parseDate(s.date);
          return d ? seasonStartYear(d) : null;
        })
        .filter((y) => y !== null) as number[],
    );
    return [...years].sort((a, b) => b - a);
  }, [sources]);
  const activeSeason = season ?? seasons[0] ?? seasonStartYear();

  const now = new Date();
  // Канонический таб матчится по category, кастомный — по членству в categories
  // (одно соревнование может быть в нескольких: Maccabiah Masters = Masters + Maccabiah).
  const inCat = (s: CompetitionSource) =>
    cat === 'all' || s.category === cat || (s.categories?.includes(cat) ?? false);
  const inSeason = (s: CompetitionSource) => {
    if (activeSeason === 'all') return true;
    const d = parseDate(s.date);
    return d ? seasonStartYear(d) === activeSeason : false;
  };
  const matchesSearch = (s: CompetitionSource) =>
    !search.trim() || s.name.toLowerCase().includes(search.trim().toLowerCase());

  const isChamp = (s: CompetitionSource) => isChampionshipSource(s);

  const visible = sources.filter(
    (s) => inCat(s) && inSeason(s) && matchesSearch(s) && (!champOnly || isChamp(s)),
  );
  const champCount = sources.filter((s) => inCat(s) && inSeason(s) && matchesSearch(s) && isChamp(s)).length;
  const nowItems = visible.filter((s) => s.status !== 'done');
  const doneItems = visible.filter((s) => s.status === 'done');
  const archivedItems = doneItems.filter((s) => isArchived(s, now));
  const shownDone = archiveOpen ? doneItems : doneItems.filter((s) => !isArchived(s, now));

  const monthGroups: { label: string; items: CompetitionSource[] }[] = [];
  shownDone.forEach((s) => {
    const label = monthLabel(s);
    let g = monthGroups.find((x) => x.label === label);
    if (!g) {
      g = { label, items: [] };
      monthGroups.push(g);
    }
    g.items.push(s);
  });

  const catCount = (key: string) =>
    key === 'all'
      ? sources.length
      : sources.filter((s) => s.category === key || (s.categories?.includes(key) ?? false)).length;

  return {
    cat, setCat,
    search, setSearch,
    season, setSeason,
    seasonTouched, setSeasonTouched,
    archiveOpen, setArchiveOpen,
    champOnly, setChampOnly,
    seasons, activeSeason,
    visible, champCount, nowItems, archivedItems, monthGroups,
    catCount,
  };
};
