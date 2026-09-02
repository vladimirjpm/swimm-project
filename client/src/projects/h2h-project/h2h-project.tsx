import React, { useMemo, useState } from 'react';
import '../../index.css';
import '../components/deep/deep-theme.css';
import './h2h-page.css';
import { useTheme } from '../../hooks/useTheme';
import { useMode } from '../../hooks/useMode';
import AppTopbar from '../components/app-topbar/app-topbar';
import UI_ModeToggle from '../components/mix/mode-toggle/mode-toggle';
import UI_H2HCompare, { h2hScopeLabel } from '../components/mix/h2h/h2h-compare';
import type { H2HSlot } from '../components/mix/h2h/h2h.types';
import { parseH2HQuery } from '../../utils/routes';
import { useSwimmerProfile, type SwimmerProfile } from '../swimmer-project/use-swimmer-profile';
import { useSwimmerCompare } from '../swimmer-project/use-swimmer-page';

/**
 * Страница `/h2h?a=&b=&season=` — сравнение ДВУХ пловцов
 * (план: `docs/plans/h2h-page-plan.md`).
 *
 * Экран здесь тот же, что в табе `?tab=h2h` страницы пловца, — общий `UI_H2HCompare`.
 * Разница только в слотах: на странице сменяемы ОБА, а на табе левый занят хозяином
 * профиля. Компонент об этой разнице не знает: он видит данные слотов, а не режим.
 *
 * Этап X3 — каркас и адрес: страница открывается, читает пару из query и показывает
 * сравнение, если оба заданы. Выбор пловцов (два слота, пикер, «ME» по умолчанию) —
 * этап X4, карусель сезонов — X5.
 */

/** Профиль → занятый слот шапки. Возраст без года рождения не показываем — его нечем проверить. */
function slotOf(profile: SwimmerProfile): H2HSlot {
  return {
    kind: 'swimmer',
    swimmer: {
      id: profile.id,
      name: profile.fullName,
      club: profile.clubName,
      ageLabel: profile.ageInSeason != null && profile.birthYear > 0
        ? `${profile.ageInSeason} y · ${profile.birthYear}`
        : profile.birthYear > 0 ? `b. ${profile.birthYear}` : null,
      avatarUrl: profile.avatarUrl,
    },
    // Избранное на странице появится вместе с выбором сторон (X4).
    isFavorite: null,
  };
}

function H2HProject() {
  useTheme();
  const { mode } = useMode();
  const themeClass = mode === 'dark' ? 'theme-deep' : 'theme-deep-light';

  // Адрес читается ОДИН раз: дальше состояние ведёт страница, а в query оно пишется
  // обратно (X4). Тот же приём, что на странице спортсмена.
  const query = useMemo(() => parseH2HQuery(), []);
  const [aId] = useState<number | null>(query.a);
  const [bId] = useState<number | null>(query.b);

  // Сезон: `undefined` в адресе — «не задан», подставляем карьеру, пока нет карусели (X5).
  const season = query.season === undefined ? null : query.season;

  const aState = useSwimmerProfile(aId);
  const bState = useSwimmerProfile(bId);
  const aProfile = aState.status === 'ok' ? aState.profile : null;
  const bProfile = bState.status === 'ok' ? bState.profile : null;

  const compare = useSwimmerCompare(aId, bId, season, aId != null && bId != null);

  const left: H2HSlot = aProfile
    ? slotOf(aProfile)
    : { kind: 'empty', label: 'בחר שחיין · choose a swimmer' };
  const right: H2HSlot = bProfile
    ? slotOf(bProfile)
    : { kind: 'empty', label: 'בחר יריב · choose a rival' };

  const notFound = (aId != null && aState.status === 'notfound')
    || (bId != null && bState.status === 'notfound');

  return (
    <div className={themeClass} style={{ background: 'var(--deep-page-bg)', minHeight: '100vh' }}>
      <AppTopbar />

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

          <UI_H2HCompare
            left={left}
            right={right}
            compare={compare.data}
            state={compare}
            emptyHint="Pick two swimmers to compare."
          />
        </div>
      </main>
    </div>
  );
}

export default H2HProject;
