import React from 'react';
import { createPortal } from 'react-dom';
import type { CompetitionSource } from '../../../../utils/helpers/competition-source';
import { competitionTileData } from '../../../../utils/helpers/competition-source';
import type { CompetitionOverview, CompetitionTab } from './types';
import UI_AddVideoIcon from '../../../components/mix/add-video-icon/add-video-icon';
import CompetitionTile from './competition-tile';
import CompetitionTabs from './competition-tabs';
import { useStickyBarSuppressed } from './sticky-chrome';

// Стики-минимизация шапки соревнования (STICKY-HEADER-MOBILE-16B «Контекст»).
// Полная шапка НЕ схлопывается и ничего не меняет в своей высоте — она просто уезжает
// вверх, а поверх контента выезжает этот компакт-бар (fixed). Две строки, ~98px:
//   1) мини-плитка + название (тап по зоне = Change) + круглая ＋ Add media,
//   2) те же табы, что в потоке (один state/URL — сюда передаётся тот же CompetitionTabs).
//
// Хендофф описывал только мобайл; на десктоп бар распространён по решению Влада
// (31.08.2026) — поведение одно на всех ширинах, меняется лишь ширина содержимого
// (общий PAGE_CONTAINER вместо края-в-край) и кегль названия.
//
// Портал в body — обязательно, а не просто position:fixed внутри шапки: шапка живёт
// внутри обёртки `relative z-40`, которая создаёт stacking context, и бар не смог бы
// подняться над топбаром (z-50). Из портала он остаётся ниже модалок (z-[100]+) и
// мобильного sheet селектора (z-[130]).

/** Один порог в обе стороны, без гистерезиса (handoff). */
const SHOW_AT = 120;

/**
 * Высота липкой шапки страницы, опубликованная в CSS-переменной: сколько сейчас занято
 * сверху — бар (когда выехал) или топбар приложения (когда бар уехал). На неё опираются
 * ЧУЖИЕ липкие строки внутри табов (переключатель All programme / My plan), чтобы не
 * прилипать под пустотой и не подлезать под топбар.
 */
const CHROME_VAR = '--comp-sticky-chrome-h';

/** Скролл страницы (тот же скроллер, что у топбара) перевалил за порог. */
function useScrolledPast(threshold: number): boolean {
  const [past, setPast] = React.useState(false);
  React.useEffect(() => {
    const onScroll = () => setPast(window.scrollY > threshold);
    onScroll();
    window.addEventListener('scroll', onScroll, { passive: true });
    return () => window.removeEventListener('scroll', onScroll);
  }, [threshold]);
  return past;
}

interface Props {
  title: string;
  overview: CompetitionOverview | null;
  source?: CompetitionSource;
  activeTab: CompetitionTab;
  onTabChange(tab: CompetitionTab): void;
  mediaCount: number | null;
  startListEntries?: number | null;
  /** undefined — гость: круглой ＋ нет (как и Add media в полной шапке). */
  onAddMedia?: () => void;
  /** Тап по зоне «плитка + название» — отдельной кнопки Change в баре нет. */
  onChangeClick?: () => void;
}

export default function CompetitionStickyBar({
  title, overview, source, activeTab, onTabChange, mediaCount, startListEntries,
  onAddMedia, onChangeClick,
}: Props) {
  // Липкая зона таба Start list, севшая под шапку, ГАСИТ бар совсем (решение Влада
  // 31.08.2026): двум липким панелям друг на друге на телефоне места нет, а наверх оттуда
  // возвращает кнопка «↑», которую показывает сама зона. Шов — sticky-chrome.ts.
  const suppressed = useStickyBarSuppressed();
  const shown = useScrolledPast(SHOW_AT) && !suppressed;
  const barRef = React.useRef<HTMLDivElement>(null);
  const tile = competitionTileData(source);

  // Публикуем занятую сверху высоту. Топбар меряем по data-крючку, а не хардкодом:
  // высота полосы задана классом в app-topbar и может измениться.
  React.useLayoutEffect(() => {
    const root = document.documentElement;
    const apply = () => {
      const topbar = document.querySelector('[data-app-topbar]');
      const height = shown
        ? (barRef.current?.getBoundingClientRect().height ?? 0)
        : (topbar?.getBoundingClientRect().height ?? 0);
      root.style.setProperty(CHROME_VAR, `${Math.round(height)}px`);
    };
    apply();
    const ro = new ResizeObserver(apply);
    if (barRef.current) ro.observe(barRef.current);
    return () => {
      ro.disconnect();
      root.style.removeProperty(CHROME_VAR);
    };
  }, [shown]);

  const identity = (
    <>
      <CompetitionTile {...tile} size="mini" />
      <span className="flex min-w-0 flex-1 items-center gap-1.5">
        {/* dir="auto" — только на тексте: на flex-строке он развернул бы раскладку бара. */}
        <span dir="auto" className="min-w-0 truncate text-[13.5px] font-extrabold sm:text-[15px]">{title}</span>
        <span className="flex-none text-[10px] opacity-85">▾</span>
      </span>
    </>
  );

  return createPortal(
    <div
      ref={barRef}
      className="fixed inset-x-0 top-0 z-[60]"
      style={{
        transform: shown ? 'translateY(0)' : 'translateY(-108%)',
        boxShadow: '0 6px 16px rgba(0,0,0,.3)',
        // visibility снимает уехавший бар с фокуса и из a11y-дерева; гасится только
        // ПОСЛЕ анимации ухода, иначе она не проиграется.
        visibility: shown ? 'visible' : 'hidden',
        transition: shown
          ? 'transform 200ms ease'
          : 'transform 200ms ease, visibility 0s linear 200ms',
      }}
    >
      <div
        style={{
          background: 'var(--theme-primary-hover, var(--theme-primary))',
          color: 'var(--theme-mode-accent-text)',
        }}
      >
        {/* Мобайл — край-в-край с паддингом 12px (макет 16b); от 640px содержимое
            садится в общий контейнер страницы, как hero и табы в потоке. */}
        <div className="mx-auto flex w-full max-w-[1440px] items-center gap-2.5 px-3 py-[7px] sm:px-4 lg:px-6">
          {onChangeClick ? (
            <button
              type="button"
              onClick={onChangeClick}
              aria-label="Change competition"
              className="flex min-w-0 flex-1 items-center gap-2.5 bg-transparent text-left"
              style={{ color: 'inherit' }}
            >
              {identity}
            </button>
          ) : (
            <span className="flex min-w-0 flex-1 items-center gap-2.5">{identity}</span>
          )}

          {onAddMedia && (
            <button
              type="button"
              onClick={onAddMedia}
              aria-label="Add media"
              className="flex h-[42px] w-[42px] flex-none items-center justify-center rounded-full"
              style={{
                background: 'var(--theme-mode-accent-text)',
                color: 'var(--theme-primary-hover, var(--theme-primary))',
              }}
            >
              {/* Та же камера-с-плюсиком, что в шапке результатов и на строках заплывов
                  (UI_AddVideoIcon), а не глиф ＋ из макета: «добавить медиа» уже имеет свой
                  знак в проекте, и второй значок для того же действия учит лишнему. */}
              <UI_AddVideoIcon size={21} />
            </button>
          )}
        </div>
      </div>

      {/* Те же табы, что в потоке: один компонент — значит один счёт и одна логика
          условных табов (Records / Start list), синхронизация даром. */}
      <CompetitionTabs
        overview={overview}
        activeTab={activeTab}
        onTabChange={onTabChange}
        mediaCount={mediaCount}
        startListEntries={startListEntries}
      />
    </div>,
    document.body,
  );
}
