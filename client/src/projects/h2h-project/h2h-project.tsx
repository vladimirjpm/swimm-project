import React, { useEffect, useMemo, useRef, useState } from 'react';
import '../../index.css';
import '../components/deep/deep-theme.css';
import './h2h-page.css';
import { useTheme } from '../../hooks/useTheme';
import { useMode } from '../../hooks/useMode';
import AppTopbar from '../components/app-topbar/app-topbar';
import UI_ModeToggle from '../components/mix/mode-toggle/mode-toggle';
import DeepSeasonCarousel from '../components/deep/season-carousel';
import UI_H2HCompare, { h2hScopeLabel } from '../components/mix/h2h/h2h-compare';
import UI_H2HRivalPicker from '../components/mix/h2h/h2h-rival-picker';
import type { H2HSlot } from '../components/mix/h2h/h2h.types';
import { parseH2HQuery, routes, H2H_PARAM } from '../../utils/routes';
import { useFavoritesContext } from '../../hooks/favorites-context';
import {
  useSwimmerProfile, type SwimmerProfile, type SwimmerSeasonOption,
} from '../swimmer-project/use-swimmer-profile';
import { useSwimmerCompare, useSwimmerSearch } from '../swimmer-project/use-swimmer-page';
import { H2H_DEFAULT_LEFT } from './h2h-settings';

/**
 * Страница `/h2h?a=&b=&season=` — сравнение ДВУХ пловцов
 * (план: `docs/plans/h2h-page-plan.md`).
 *
 * Экран здесь тот же, что в табе `?tab=h2h` страницы пловца, — общий `UI_H2HCompare`.
 * Разница только в слотах: тут сменяемы ОБА и есть «поменять местами», а на табе левый
 * занят хозяином профиля. Компонент об этой разнице не знает: он видит данные слотов.
 *
 * Выбор устроен как «активный слот»: пикер один, а какую сторону он заполнит — решает
 * последний клик по пустому слоту или крестику. Два пикера рядом читались бы как два
 * разных поиска, хотя ищут они одно и то же.
 */

/** Какую сторону сейчас заполняет пикер. */
type ActiveSide = 'a' | 'b';

/**
 * Сезоны карусели — ОБЪЕДИНЕНИЕ сезонов обоих пловцов, а не пересечение: у соперника
 * бывают сезоны, которых нет у первого, и пересечение прятало бы данные, которые есть
 * у одного. Дубли схлопываются по году, метка у сезона одна и та же.
 */
function mergeSeasons(a?: SwimmerSeasonOption[], b?: SwimmerSeasonOption[]): SwimmerSeasonOption[] {
  const byYear = new Map<number, SwimmerSeasonOption>();
  [...(a ?? []), ...(b ?? [])].forEach((option) => {
    const known = byYear.get(option.season);
    // Витринный флаг и «текущий» сохраняем, если он есть хотя бы у одной стороны.
    byYear.set(option.season, known
      ? {
        ...known,
        isCurrent: known.isCurrent || option.isCurrent,
        isDisplayDefault: known.isDisplayDefault || option.isDisplayDefault,
        swims: known.swims + option.swims,
      }
      : option);
  });
  return [...byYear.values()].sort((x, y) => y.season - x.season);
}

/** Профиль → занятый слот шапки. Возраст без года рождения не показываем — его нечем проверить. */
function slotSwimmer(profile: SwimmerProfile) {
  return {
    id: profile.id,
    name: profile.fullName,
    club: profile.clubName,
    ageLabel: profile.ageInSeason != null && profile.birthYear > 0
      ? `${profile.ageInSeason} y · ${profile.birthYear}`
      : profile.birthYear > 0 ? `b. ${profile.birthYear}` : null,
    avatarUrl: profile.avatarUrl,
    gender: profile.gender,
    countryCode: profile.countryCode,
  };
}

function H2HProject() {
  useTheme();
  const { mode } = useMode();
  const themeClass = mode === 'dark' ? 'theme-deep' : 'theme-deep-light';

  const {
    isAuthenticated, favorites, primarySwimmerId, favoriteSwimmerIds, toggleFavoriteSwimmer,
  } = useFavoritesContext();

  // Адрес читается ОДИН раз: дальше состояние ведёт страница, а в query пишется обратно.
  const query = useMemo(() => parseH2HQuery(), []);
  const [aId, setAId] = useState<number | null>(query.a);
  const [bId, setBId] = useState<number | null>(query.b);
  const [active, setActive] = useState<ActiveSide>(query.a == null ? 'a' : 'b');
  const [search, setSearch] = useState('');
  const searchRef = useRef<HTMLInputElement>(null);

  // `undefined` — сезон ещё не выбран (ждём профили), `null` — режим ∞ (карьера).
  const [season, setSeason] = useState<number | null | undefined>(query.season);

  /**
   * «ME» в левом слоте — только когда адрес молчит про `a` (H2H_DEFAULT_LEFT).
   * В адрес это НЕ пишется: пустая страница должна оставаться `/h2h`, иначе ссылкой на неё
   * человек передал бы своего пловца.
   */
  useEffect(() => {
    if (H2H_DEFAULT_LEFT !== 'me') return;
    if (query.a != null || aId != null || primarySwimmerId == null) return;
    setAId(primarySwimmerId);
    setActive('b');
  }, [primarySwimmerId, query.a, aId]);

  const aState = useSwimmerProfile(aId);
  const bState = useSwimmerProfile(bId);
  const aProfile = aState.status === 'ok' ? aState.profile : null;
  const bProfile = bState.status === 'ok' ? bState.profile : null;

  const seasons = useMemo(
    () => mergeSeasons(aProfile?.seasons, bProfile?.seasons),
    [aProfile?.seasons, bProfile?.seasons],
  );

  /**
   * Умолчание — ВИТРИННЫЙ сезон (до зимних чемпионатов это прошлый, а не свежий:
   * docs/season-boundary-rule.md). Сервер помечает его `isDisplayDefault` в профиле; при
   * двух профилях берём первый попавшийся помеченный, иначе — самый свежий общий сезон.
   *
   * Сезон из адреса сильнее: пришли по ссылке — открылось ровно то, что в ней.
   */
  useEffect(() => {
    if (season !== undefined || seasons.length === 0) return;
    const preferred = seasons.find((x) => x.isDisplayDefault) ?? seasons[0];
    setSeason(preferred.season);
  }, [seasons, season]);

  // Пока сезон не определён, запрос не шлём: иначе первый кадр уехал бы за карьеру, а
  // вторым пришёл сезон — и панель дважды перерисовалась бы другими цифрами.
  const seasonReady = season !== undefined;
  const activeSeason = seasonReady ? season : null;

  const compare = useSwimmerCompare(
    aId, bId, activeSeason, seasonReady && aId != null && bId != null);
  const found = useSwimmerSearch(search);
  // Уже занятые стороны из выдачи убираем: выбор «того же самого» во второй слот выглядел
  // бы как перескок пловца через центр, а сравнения с самим собой всё равно не бывает.
  const hits = useMemo(
    () => (found.data ?? []).filter((h) => h.id !== aId && h.id !== bId),
    [found.data, aId, bId],
  );

  /** Пара и сезон в адресе: пустые слоты не пишем — `/h2h` это законное «ещё никого». */
  const writeQuery = (next: { a?: number | null; b?: number | null; season?: number | null }) => {
    const url = new URL(window.location.href);
    const apply = (key: string, value: number | null | undefined) => {
      if (value === undefined) return;
      if (value == null) url.searchParams.delete(key);
      else url.searchParams.set(key, String(value));
    };
    // Имена сторон — из общего контракта (`H2H_PARAM`), а не строками здесь: этот же
    // адрес читает `parseH2HQuery`, и разъехаться им нельзя. Легаси `?a=&b=` вычищаем,
    // иначе адрес нёс бы обе пары имён.
    url.searchParams.delete('a');
    url.searchParams.delete('b');
    apply(H2H_PARAM.a, next.a);
    apply(H2H_PARAM.b, next.b);
    // `season=all` — режим карьеры; отсутствие параметра значит «не выбран», и это разные
    // состояния, поэтому ∞ пишется явным словом, а не удалением параметра.
    if (next.season !== undefined) url.searchParams.set('season', next.season == null ? 'all' : String(next.season));
    window.history.replaceState(null, '', url.toString());
  };

  const handleSeason = (next: number | null) => {
    setSeason(next);
    writeQuery({ season: next });
  };

  const pick = (id: number) => {
    // Тот же пловец с другой стороны — не сравнение, а зеркало: молча меняем стороны
    // местами вместо того, чтобы поставить его дважды.
    if (active === 'a') {
      const nextB = bId === id ? aId : bId;
      setAId(id); setBId(nextB); writeQuery({ a: id, b: nextB });
      setActive(nextB == null ? 'b' : 'a');
    } else {
      const nextA = aId === id ? bId : aId;
      setBId(id); setAId(nextA); writeQuery({ a: nextA, b: id });
      setActive('a');
    }
    setSearch('');
  };

  const clear = (side: ActiveSide) => {
    if (side === 'a') { setAId(null); writeQuery({ a: null }); } else { setBId(null); writeQuery({ b: null }); }
    setActive(side);
    setSearch('');
    // Фокус в поиск: крестик — это «выбрать другого», а не «просто убрать».
    window.setTimeout(() => searchRef.current?.focus(), 0);
  };

  const swap = () => {
    setAId(bId); setBId(aId);
    writeQuery({ a: bId, b: aId });
  };

  const focusSlot = (side: ActiveSide) => {
    setActive(side);
    searchRef.current?.focus();
  };

  const favProps = (id: number) => ({
    isFavorite: isAuthenticated ? favoriteSwimmerIds.has(id) : null,
    onToggleFavorite: () => toggleFavoriteSwimmer(id),
  });

  const slotOf = (side: ActiveSide, profile: SwimmerProfile | null, id: number | null): H2HSlot => {
    if (id == null) {
      return {
        kind: 'empty',
        label: side === 'a' ? 'בחר שחיין · choose a swimmer' : 'בחר יריב · choose a rival',
        onPick: () => focusSlot(side),
      };
    }
    return {
      kind: 'swimmer',
      // Профиль ещё едет — показываем слот с номером, чтобы шапка не прыгала при загрузке.
      swimmer: profile ? slotSwimmer(profile) : { id, name: `#${id}` },
      ...favProps(id),
      onClear: () => clear(side),
    };
  };

  // Избранное как быстрый выбор: обе стороны, уже занятые, из списка убираем.
  const favoriteChips = favorites
    .filter((f) => f.target_type === 'swimmer' && f.swimmer_id != null
      && f.swimmer_id !== aId && f.swimmer_id !== bId)
    .sort((x, y) => Number(y.is_primary) - Number(x.is_primary) || x.sort_order - y.sort_order)
    .map((f) => ({ id: f.swimmer_id!, name: f.swimmer_name ?? `#${f.swimmer_id}` }));

  const notFound = (aId != null && aState.status === 'notfound')
    || (bId != null && bState.status === 'notfound');

  const picker = (
    <div className="h2h-page__picker">
      <div className="h2h-page__picker-cap">
        Choosing the <strong>{active === 'a' ? 'left' : 'right'}</strong> swimmer
      </div>
      <UI_H2HRivalPicker
        favorites={favoriteChips}
        query={search}
        onQuery={setSearch}
        hits={found.data ? hits : null}
        loading={found.loading}
        error={found.error}
        onPick={pick}
        inputRef={searchRef}
        // Нашли, но всех отфильтровали — значит найденный уже стоит в слоте.
        emptyText={(found.data?.length ?? 0) > 0 ? 'Already on the board.' : 'Nobody found.'}
      />
    </div>
  );

  return (
    <div className={themeClass} style={{ background: 'var(--deep-page-bg)', minHeight: '100vh' }}>
      <AppTopbar active="h2h" />

      <main className="mx-auto max-w-[1180px] px-4 py-6" style={{ color: 'var(--deep-text)' }}>
        <div className="mb-4 flex justify-end">
          <UI_ModeToggle />
        </div>

        <div className="h2h-page">
          <div className="h2h-page__head">
            <h1 className="h2h-page__title">Head to head</h1>
            <div className="h2h-page__hint">
              {compare.data
                ? h2hScopeLabel(compare.data)
                : 'two swimmers side by side, distance by distance'}
            </div>
          </div>

          {notFound && <div className="h2h-page__notice">Swimmer not found.</div>}

          {/* Карусель появляется вместе с первым пловцом: без него сезонов нет и выбирать
              нечего. Список — объединение сезонов обоих (см. mergeSeasons). */}
          {seasons.length > 0 && (
            <DeepSeasonCarousel
              seasons={seasons}
              season={activeSeason}
              onSeason={handleSeason}
            />
          )}

          <UI_H2HCompare
            left={slotOf('a', aProfile, aId)}
            right={slotOf('b', bProfile, bId)}
            compare={compare.data}
            state={compare}
            picker={picker}
            emptyHint="Pick two swimmers to compare."
            // Менять стороны местами можно только когда есть что менять.
            onSwap={aId != null && bId != null ? swap : undefined}
          />

          {aId != null && bId != null && (
            <a className="h2h-page__profile-link" href={routes.swimmer(aId)}>
              Open profile of the left swimmer →
            </a>
          )}
        </div>
      </main>
    </div>
  );
}

export default H2HProject;
