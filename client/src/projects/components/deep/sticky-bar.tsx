import React, { useEffect, useLayoutEffect, useState } from 'react';
import type { EntityStickyBar } from './entity-page-types';

/**
 * Липкая полоса страницы сущности (хендофф group-club-changes §5).
 *
 * Шапка сущности длинная, и уже после первого экрана непонятно, чья это страница. Поэтому
 * после прокрутки (телефон > 300px, десктоп > 200px) под топбаром выезжает полоса: аватар,
 * имя одной строкой и статус справа. Ряд табов липнет под ней (`--deep-sticky-top`, его
 * публикует этот хук — см. `useEntitySticky`).
 *
 * Тап по полосе — наверх: `window.scrollTo`, не `scrollIntoView` (второй у липкого топбара
 * уводит начало страницы под полосу).
 *
 * Портал не нужен: предки полосы не создают stacking context, и `fixed` + z-40 ложится под
 * топбар (z-50), но над контентом и липким рядом табов.
 */

/** Порог показа: у телефона шапка выше — и порог дальше. */
const THRESHOLD_MOBILE = 300;
const THRESHOLD_DESKTOP = 200;
const DESKTOP_QUERY = '(min-width: 960px)';

/** Высоты полосы — те же, что задаёт `.deep-sticky-bar` в deep-theme.css. */
const BAR_H_MOBILE = 48;
const BAR_H_DESKTOP = 52;

/**
 * Состояние липкой полосы: выехала ли и где начинается зона под ней. `top` — высота топбара
 * приложения (меряем живую по data-крючку, а не хардкодом), `stickyTop` — куда липнет ряд
 * табов: под топбар, а когда полоса выехала — под неё.
 */
function useEntitySticky(enabled: boolean): { shown: boolean; top: number; stickyTop: number } {
  const [shown, setShown] = useState(false);
  const [top, setTop] = useState(46);
  const [desktop, setDesktop] = useState(() => window.matchMedia(DESKTOP_QUERY).matches);

  useLayoutEffect(() => {
    const topbar = document.querySelector('[data-app-topbar]');
    if (!topbar) return undefined;
    const apply = () => setTop(Math.round(topbar.getBoundingClientRect().height));
    apply();
    const ro = new ResizeObserver(apply);
    ro.observe(topbar);
    return () => ro.disconnect();
  }, []);

  useEffect(() => {
    if (!enabled) { setShown(false); return undefined; }
    const mq = window.matchMedia(DESKTOP_QUERY);
    const onScroll = () => {
      setDesktop(mq.matches);
      setShown(window.scrollY > (mq.matches ? THRESHOLD_DESKTOP : THRESHOLD_MOBILE));
    };
    onScroll();
    window.addEventListener('scroll', onScroll, { passive: true });
    mq.addEventListener('change', onScroll);
    return () => {
      window.removeEventListener('scroll', onScroll);
      mq.removeEventListener('change', onScroll);
    };
  }, [enabled]);

  const barH = desktop ? BAR_H_DESKTOP : BAR_H_MOBILE;
  return { shown, top, stickyTop: top + (shown ? barH : 0) };
}

function DeepStickyBar({ bar, shown, top }: { bar: EntityStickyBar; shown: boolean; top: number }) {
  return (
    <div
      className="deep-sticky-bar"
      style={{
        top,
        transform: shown ? 'translateY(0)' : 'translateY(-110%)',
        // visibility снимает уехавшую полосу с фокуса и из a11y-дерева; гасится только ПОСЛЕ
        // анимации ухода, иначе она не проиграется (как у полосы соревнования).
        visibility: shown ? 'visible' : 'hidden',
        transition: shown ? 'transform 180ms ease' : 'transform 180ms ease, visibility 0s linear 180ms',
      }}
    >
      <button
        type="button"
        className="deep-sticky-bar__inner"
        onClick={() => window.scrollTo({ top: 0, behavior: 'smooth' })}
        aria-label="Back to top"
      >
        <span className="deep-sticky-bar__avatar">{bar.avatar}</span>
        {/* dir="auto": у ивритского имени многоточие встаёт в логический конец (слева), и
            начало имени видно; латинское остаётся LTR и скобок не переставляет. */}
        <span dir="auto" className="deep-sticky-bar__name">{bar.name}</span>
        {bar.nameEn && bar.nameEn !== bar.name && (
          <span className="deep-sticky-bar__name-en">{bar.nameEn}</span>
        )}
        {bar.status && <span className="deep-sticky-bar__status">{bar.status}</span>}
      </button>
    </div>
  );
}

export default DeepStickyBar;
export { useEntitySticky };
