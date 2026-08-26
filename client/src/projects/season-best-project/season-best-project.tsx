import React, { useMemo } from 'react';
import '../../index.css';
import '../components/deep/deep-theme.css';
import './season-best-page.css';
import { useTheme } from '../../hooks/useTheme';
import { useMode } from '../../hooks/useMode';
import AppTopbar from '../components/app-topbar/app-topbar';
import UI_ModeToggle from '../components/mix/mode-toggle/mode-toggle';
import { parseSeasonBestQuery, routes } from '../../utils/routes';
import { seasonLabel } from '../../utils/helpers/season-helper';

/**
 * Страница `/season-best` — списки «лучшие в сезоне»: кто быстрее всех в связке
 * возраст × пол × стиль × дистанция × бассейн.
 *
 * СЕЙЧАС ЭТО ЗАГОТОВКА: маршрут, разбор адреса и шапка работают, самих списков ещё нет.
 * Заведена она раньше содержимого сознательно — ссылки на неё уже стоят в таблице Season
 * best страницы спортсмена, и без страницы они молча уводили бы на домашнюю (dev-фоллбек
 * Vite отдаёт index.html на любой нераспознанный чистый путь, то есть 200 с чужим экраном).
 *
 * Весь фильтр живёт в query и читается ТОЛЬКО через `parseSeasonBestQuery` — у списка нет
 * идентичности в пути, адресом его делает набор параметров (правило routes.ts).
 *
 * Данные, когда дойдут руки, уже есть на сервере: `GetAgeCohortSeasonBestsAsync` возвращает
 * лучшие времена ВСЕЙ возрастной когорты за сезон, а не только одного пловца, — списку нужна
 * ровно эта выборка, отфильтрованная по ключу дисциплины.
 */

/** «individual_medley» → «individual medley»: ключ стиля приходит машинным. */
const strokeLabel = (stroke: string) => stroke.replace(/_/g, ' ');

/** Подпись группы сверстников — та же формула, что на сервере (SwimmerPageBuilder). */
function groupLabel(age: number | null, gender: 'male' | 'female' | null): string | null {
  if (age == null || gender == null) return null;
  const adult = age >= 18;
  const noun = gender === 'female' ? (adult ? 'women' : 'girls') : (adult ? 'men' : 'boys');
  return `${noun} ${age}`;
}

function SeasonBestProject() {
  useTheme();
  const { mode } = useMode();

  const query = useMemo(() => parseSeasonBestQuery(), []);
  const themeClass = mode === 'dark' ? 'theme-deep' : 'theme-deep-light';

  const group = groupLabel(query.age, query.gender);
  const event = [
    query.distance,
    query.stroke ? strokeLabel(query.stroke) : null,
  ].filter(Boolean).join(' ');

  return (
    <div className={themeClass} style={{ background: 'var(--deep-page-bg)', minHeight: '100vh' }}>
      <AppTopbar />

      <main className="mx-auto max-w-[1180px] px-4 py-6" style={{ color: 'var(--deep-text)' }}>
        <div className="mb-4 flex justify-end">
          <UI_ModeToggle />
        </div>

        <div className="sb-head">
          <div className="sb-head__title">
            Season best{group ? ` — ${group}` : ''}
          </div>
          <div className="sb-head__sub">
            {/* Показываем РАЗОБРАННЫЙ адрес, а не выдуманные умолчания: пока списков нет,
                единственная польза страницы — доказать, что ссылка донесла свой срез. */}
            {[
              query.season != null ? `season ${seasonLabel(query.season)}` : 'season not set',
              event || 'all events',
              query.poolType ?? 'any pool',
            ].join(' · ')}
          </div>
        </div>

        <div className="sb-empty">
          <div className="sb-empty__title">Nothing here yet</div>
          <p className="sb-empty__text">
            This page will list everyone in the age group for the chosen event and season.
            It is not built yet — the address above is already carrying the right slice.
          </p>
          {query.swimmerId != null && (
            <a className="sb-empty__back" href={`${routes.swimmer(query.swimmerId)}?view=season-best`}>
              ← Back to the swimmer
            </a>
          )}
        </div>
      </main>
    </div>
  );
}

export default SeasonBestProject;
