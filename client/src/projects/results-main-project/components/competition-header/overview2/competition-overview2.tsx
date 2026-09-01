import React, { useCallback, useEffect, useMemo, useState } from 'react';
import type { CompetitionOverview } from '../types';
import type { CompetitionMediaItem } from '../../../../../hooks/useCompetitionMedia';
import { useFavoritesContext } from '../../../../../hooks/favorites-context';
import { MODULE_ORDER, MODULE_LABEL, isModuleKey, initials, type ModuleKey } from './module-defs';
import ModuleTile from './module-tile';
import ModuleCardClubs from './module-card-clubs';
import ModuleCardRecords from './module-card-records';
import ModuleCardChampions from './module-card-champions';
import ModuleCardHpa from './module-card-hpa';
import ModuleCardMedia from './module-card-media';
import ModuleCardFavorites from './module-card-favorites';
import UI_ClubIcon from '../../../../components/mix/club-icon/club-icon';
import './overview2.css';

// Таб Overview 2 (design_handoff_competition_overview2, вариант 9d десктоп + 9f мобайл):
// мастер-детейл — колонка плиток модулей 308px + большая карточка активного модуля.
// <lg — аккордеон: карточка рендерится ПОД своей плиткой. State один: activeModule,
// в URL — ?panel= (диплинк открывает нужную карточку). Цвета — только тем-токены
// --theme-module-* через overview2.css.

interface Props {
  overview: CompetitionOverview | null;
  loading: boolean;
  mediaItems: CompetitionMediaItem[];
  onOpenTab(tab: 'swims' | 'clubs' | 'media' | 'records'): void;
  onOpenSwim?(swim: { result_id: number | null; style_name: string; distance: string }): void;
  onOpenClub?(club: string): void;
  /** Переход в Swims со скоупом my|favorites (карточка Favorites). */
  onOpenSwimsScoped?(scope: 'my' | 'favorites'): void;
  onAddMedia?: () => void;
}

function readPanelFromUrl(): ModuleKey | null {
  const p = new URLSearchParams(window.location.search).get('panel');
  return isModuleKey(p) ? p : null;
}


export default function CompetitionOverview2({
  overview, loading, mediaItems, onOpenTab, onOpenSwim, onOpenClub, onOpenSwimsScoped, onAddMedia,
}: Props) {
  const { isAuthenticated, primarySwimmerId, favoriteSwimmerIds } = useFavoritesContext();

  // Ненаградное соревнование (лига, отбор): места в протоколе есть, награждения нет.
  // Всё медальное гасится ПРОПСАМИ существующих карточек, а не отдельными компонентами.
  const hasAwards = overview?.has_awards ?? true;

  // --- какие модули видимы (пустые скрываются по-отдельности) ---
  const visible = useMemo(() => {
    const set = new Set<ModuleKey>();
    if (!overview) return set;
    if (overview.top_clubs.length) set.add('clubs');
    if (overview.records.length) set.add('records');
    // Без награждения карточку Champions держит только Best swim: медалисты в ней скрыты,
    // и модуль из одних медалистов оказался бы пустым.
    const hasBestSwim = !!(overview.best_swim || overview.best_swim_male || overview.best_swim_female);
    const hasMedalists = !!(overview.top_medalists?.length || overview.top_medalists_male?.length
      || overview.top_medalists_female?.length);
    if (hasBestSwim || (hasAwards && hasMedalists)) set.add('champions');
    if (overview.high_point_awards.length) set.add('hpa');
    if (mediaItems.length) set.add('media');
    if (isAuthenticated && (primarySwimmerId != null || favoriteSwimmerIds.size > 0)) set.add('favorites');
    return set;
  }, [overview, hasAwards, mediaItems.length, isAuthenticated, primarySwimmerId, favoriteSwimmerIds]);

  // Модули, которые видны, но не открываются: High Point без награждения не разыгрывается.
  const isDisabled = useCallback((m: ModuleKey) => m === 'hpa' && !hasAwards, [hasAwards]);

  const modules = MODULE_ORDER.filter((m) => visible.has(m));

  // --- state активного модуля + синк с ?panel= ---
  const [active, setActive] = useState<ModuleKey | null>(readPanelFromUrl);

  // Выбранный модуль засчитывается, только если он видим и не выключен: и диплинк
  // ?panel=, и прошлый выбор не должны упираться в пустую карточку.
  const chosen: ModuleKey | null =
    active && visible.has(active) && !isDisabled(active) ? active : null;

  // ДЕФОЛТ РАЗНЫЙ ПО МАКЕТАМ, и решает это CSS, а не JS-медиазапрос: оба макета всегда
  // в DOM, видимость даёт брейкпоинт lg. Мобайл-аккордеон открытым по умолчанию не бывает
  // (первая карточка отжимала бы остальные плитки за экран), а у мастер-детейла правая
  // половина обязана что-то показывать — там дефолт остаётся первым доступным модулем.
  const desktopActive: ModuleKey | null = chosen ?? modules.find((m) => !isDisabled(m)) ?? null;
  const mobileActive: ModuleKey | null = chosen;

  const selectDesktop = useCallback((m: ModuleKey) => setActive(m), []);
  // В аккордеоне повторный тап по открытой плитке закрывает её — раз «всё закрыто»
  // штатное состояние, в него надо уметь вернуться.
  const selectMobile = useCallback(
    (m: ModuleKey) => setActive((prev) => (prev === m ? null : m)),
    [],
  );

  // ?panel= пишем ТОЛЬКО по явному выбору пользователя, а не по дефолту: какой макет
  // сейчас на экране, JS тут не знает, и ссылка не должна обещать карточку, которой
  // в мобайле не открыто. При уходе с таба параметр чистит results-main-project
  // (см. handleCompTabChange).
  useEffect(() => {
    const url = new URL(window.location.href);
    if (chosen) {
      if (readPanelFromUrl() === chosen) return;
      url.searchParams.set('panel', chosen);
    } else {
      if (!url.searchParams.has('panel')) return;
      url.searchParams.delete('panel');
    }
    window.history.replaceState(null, '', url.toString());
  }, [chosen]);

  if (!overview) {
    return (
      <div className="mt-4 flex min-h-[140px] items-center justify-center rounded-[14px] text-[13px] font-semibold"
        style={{
          background: 'var(--theme-mode-surface)', boxShadow: 'var(--theme-mode-card-shadow)',
          color: 'var(--theme-mode-text-muted)',
        }}>
        {loading ? 'Loading overview…' : 'No overview data for this competition.'}
      </div>
    );
  }

  // --- итоги на плитках (9d): медальон + имя лидера + подпись ---
  const leaderClub = overview.top_clubs[0] ?? null;

  const recordsByHolder = new Map<string, number>();
  for (const r of overview.records) {
    recordsByHolder.set(r.holder_name, (recordsByHolder.get(r.holder_name) ?? 0) + 1);
  }
  const topHolder = [...recordsByHolder.entries()].sort((a, b) => b[1] - a[1])[0] ?? null;

  const bestSwims = [overview.best_swim_male, overview.best_swim_female, overview.best_swim]
    .filter((s): s is NonNullable<typeof s> => s != null);
  const champion = bestSwims.sort((a, b) => b.international_points - a.international_points)[0] ?? null;

  const hpaAges = overview.high_point_awards.map((a) => a.age);
  const hpaRange = hpaAges.length ? `${Math.min(...hpaAges)}–${Math.max(...hpaAges)}` : '';

  const videoCount = mediaItems.filter((m) => m.type === 'video').length;
  const photoCount = mediaItems.length - videoCount;

  const favCount = favoriteSwimmerIds.size;

  const tileProps: Record<ModuleKey, {
    medal: React.ReactNode; medalIsLogo?: boolean; name: React.ReactNode; sub?: React.ReactNode;
  }> = {
    clubs: {
      // Лого клуба-лидера кругом (handoff v3): UI_ClubIcon сам падает на no-club.png,
      // без клуба остаётся полосатый плейсхолдер с инициалами.
      medal: leaderClub
        ? <UI_ClubIcon clubName={leaderClub.club} clubId={leaderClub.clubId} iconWidth="full" />
        : '—',
      medalIsLogo: true,
      name: leaderClub?.club ?? '—',
      sub: leaderClub ? `${leaderClub.points} · ${overview.summary.club_count} clubs` : undefined,
    },
    records: {
      medal: topHolder?.[1] ?? 0,
      name: topHolder?.[0] ?? '—',
      sub: `${overview.records.length} records · ${recordsByHolder.size} swimmers`,
    },
    champions: {
      medal: champion?.international_points ?? '—',
      name: champion ? `${champion.first_name} ${champion.last_name}`.trim() : '—',
      sub: champion ? `max points · ${champion.gender === 'male' ? '♂' : '♀'}` : undefined,
    },
    hpa: {
      // Без награждения плитка гасится: обещать «12 awards» там, где наград нет, нельзя.
      medal: hasAwards ? (hpaRange || '—') : '—',
      name: hasAwards ? `${overview.high_point_awards.length} awards` : 'Not contested',
      sub: hasAwards ? 'each age · ♂ / ♀' : 'no awards at this competition',
    },
    media: {
      medal: mediaItems.length,
      name: `${videoCount} video · ${photoCount} photo`,
      sub: 'gallery',
    },
    favorites: {
      medal: '❤️',
      name: `${favCount} swimmer${favCount === 1 ? '' : 's'}`,
      sub: 'who I follow',
    },
  };

  const renderCard = (m: ModuleKey) => {
    switch (m) {
      case 'clubs':
        // Без награждения — голый зачёт очков: ни медалей, ни шкалы правила, ни ♂/♀.
        return (
          <ModuleCardClubs
            overview={overview}
            onOpenTab={onOpenTab}
            onOpenClub={onOpenClub}
            showMedals={hasAwards}
            showPointsRule={hasAwards}
            showGenderPanels={hasAwards}
          />
        );
      case 'records':
        return <ModuleCardRecords overview={overview} onOpenTab={onOpenTab} onOpenSwim={onOpenSwim} />;
      case 'champions':
        return (
          <ModuleCardChampions
            overview={overview}
            onOpenSwim={onOpenSwim}
            onOpenClub={onOpenClub}
            showMostDecorated={hasAwards}
          />
        );
      case 'hpa':
        return <ModuleCardHpa overview={overview} onOpenClub={onOpenClub} />;
      case 'media':
        return (
          <ModuleCardMedia
            items={mediaItems}
            isAuthenticated={isAuthenticated}
            onAddMedia={onAddMedia}
            onOpenTab={onOpenTab}
          />
        );
      case 'favorites':
        return (
          <ModuleCardFavorites
            overview={overview}
            onOpenSwimsScoped={onOpenSwimsScoped}
            onOpenClub={onOpenClub}
          />
        );
      default:
        return null;
    }
  };

  const tile = (m: ModuleKey, activeKey: ModuleKey | null, onSelect: (m: ModuleKey) => void) => (
    <ModuleTile
      key={m}
      module={m}
      active={m === activeKey}
      onSelect={onSelect}
      disabled={isDisabled(m)}
      disabledReason="No awards at this competition — High Point is not contested"
      {...tileProps[m]}
    />
  );

  return (
    <div className="mt-4">
      {/* Десктоп 9d: колонка плиток 308px + карточка справа */}
      <div className="hidden lg:grid lg:grid-cols-[308px_minmax(0,1fr)] lg:items-start lg:gap-4">
        <div
          role="tablist"
          aria-label="Overview modules"
          className="flex flex-col gap-0.5 rounded-[12px] p-0 overflow-visible"
          style={{ background: 'var(--theme-mode-border)' }}
        >
          {modules.map((m) => tile(m, desktopActive, selectDesktop))}
        </div>
        <div className="min-w-0">
          {desktopActive && (
            <div className={`ov2-module ov2-module--${desktopActive}`}>{renderCard(desktopActive)}</div>
          )}
        </div>
      </div>

      {/* Мобайл 9f: аккордеон — карточка ПОД своей плиткой; по умолчанию всё закрыто */}
      <div className="flex flex-col gap-1 lg:hidden" role="tablist" aria-label="Overview modules">
        {modules.map((m) => (
          <React.Fragment key={m}>
            {tile(m, mobileActive, selectMobile)}
            {m === mobileActive && (
              <div className={`ov2-module ov2-module--${m} mt-1 mb-1`}>{renderCard(m)}</div>
            )}
          </React.Fragment>
        ))}
      </div>
    </div>
  );
}
