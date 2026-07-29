import React from 'react';
import '../home-project/home.css';
import AppTopbar from '../components/app-topbar/app-topbar';
import RecordTicker from '../home-project/components/record-ticker';
import { HOME_REGION_LABEL } from '../../utils/constants/home-region';
import {
  CompetitionSource,
  parseDate,
  dateLabel,
} from '../../utils/helpers/competition-source';
import { routes } from '../../utils/routes';

type CompetitionLink = {
  href: string;
  title: string;
  subtitle: string;
  badge: string;
  live?: boolean;
};

const PAGES: CompetitionLink[] = [
  {
    href: routes.competition('last'),
    title: 'Latest meet',
    subtitle: 'Свежие результаты последнего соревнования',
    badge: '● LIVE',
    live: true,
  },
  // Ступени: Kids 8–11, Youth 11–14, Juniors (בוגרים). Ключи в href исторические и с
  // подписями не совпадают — они в закладках пользователей (см. results-categories.ts).
  {
    href: `${routes.results()}?category=young8_11`,
    title: 'Kids',
    subtitle: 'Competitions for ages 8–11',
    badge: '8-11',
  },
  {
    href: `${routes.results()}?category=junior`,
    title: 'Youth',
    subtitle: 'Competitions for ages 11–14',
    badge: '11-14',
  },
  {
    href: `${routes.results()}?category=adults`,
    title: 'Juniors',
    subtitle: 'Israeli championship competitions',
    badge: 'J',
  },
  {
    href: `${routes.results()}?category=masters`,
    title: 'Masters',
    subtitle: 'Masters meets incl. Dolphin, ages 21+',
    badge: 'М',
  },
  {
    href: `${routes.results()}?category=all`,
    title: 'All results',
    subtitle: 'Every competition in one list',
    badge: '∀',
  },
];

// Секция «Meets» — живой список из /api/competitions: live/upcoming всегда + завершённые
// за текущий и предыдущий календарный месяц (по дате начала). Архив/пагинация — вне скоупа
// (см. multiday-client-grouping-sonnet.md); полный список есть в селекторе results_main.
const STATUS_ORDER: Record<CompetitionSource['status'], number> = { live: 0, upcoming: 1, done: 2 };

function MeetsSection() {
  const [sources, setSources] = React.useState<CompetitionSource[]>([]);
  const [loaded, setLoaded] = React.useState(false);

  React.useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const resp = await fetch('/api/competitions', { credentials: 'same-origin' });
        if (!resp.ok) throw new Error(`API /api/competitions вернул ${resp.status}`);
        const data = (await resp.json()) as CompetitionSource[];
        if (!cancelled) setSources(data);
      } catch (e) {
        console.error('Error loading /api/competitions', e);
        if (!cancelled) setSources([]);
      } finally {
        if (!cancelled) setLoaded(true);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, []);

  if (!loaded || sources.length === 0) return null;

  const now = new Date();
  const monthBoundary = new Date(now.getFullYear(), now.getMonth() - 1, 1);

  const visible = sources
    .filter((s) => {
      if (s.status !== 'done') return true;
      const d = parseDate(s.date);
      return !!d && d >= monthBoundary;
    })
    // Стабильная сортировка: /api/competitions уже отдаёт даты desc, здесь только
    // перегруппировываем по статусу (live → upcoming → done), сохраняя порядок внутри группы.
    .sort((a, b) => STATUS_ORDER[a.status] - STATUS_ORDER[b.status]);

  if (visible.length === 0) return null;

  return (
    <section className="relative px-4 pt-[38px] lg:px-16 lg:pt-16" aria-label="Meets">
      <p className="mb-[14px] text-[11px] font-extrabold uppercase tracking-[0.28em] text-[#7dd3fc] lg:text-[13px] lg:tracking-[0.3em]">
        {`${now.getFullYear()} Season · ${HOME_REGION_LABEL}`}
      </p>
      <h2 className="mb-[18px] text-[26px] font-black tracking-[-0.02em] text-[#f3f8fd] lg:mb-6 lg:text-[36px]">
        Meets
      </h2>
      <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3 lg:gap-[18px]">
        {visible.map((src) => {
          const href = src.kind === 'event'
            ? `${routes.results()}?eventId=${src.id}`
            : routes.competition(src.id);
          return (
            <a
              key={`${src.kind}:${src.id}`}
              href={href}
              className="hp-card-std flex min-h-[110px] flex-col justify-between gap-2 rounded-[16px] border border-[#7dd3fc]/[0.22] p-4 text-inherit no-underline shadow-[0_18px_44px_rgba(2,10,24,0.45)] backdrop-blur-[14px] transition-[transform,border-color,box-shadow] duration-[180ms] ease-out hover:-translate-y-1 hover:border-[#7dd3fc]/80 hover:shadow-[0_24px_50px_rgba(2,10,24,0.6)] focus-visible:-translate-y-1 focus-visible:border-[#7dd3fc]/80 focus-visible:outline focus-visible:outline-[3px] focus-visible:outline-offset-[3px] focus-visible:outline-[#7dd3fc]"
            >
              <div className="flex items-start justify-between gap-2">
                <span
                  dir="rtl"
                  className="min-w-0 overflow-hidden text-ellipsis whitespace-nowrap text-[15px] font-extrabold text-[#f3f8fd]"
                  style={{ textAlign: 'left' }}
                >
                  {src.name}
                </span>
                {src.status === 'live' ? (
                  <span className="hp-mono flex shrink-0 items-center gap-1 rounded-[7px] border border-[#38ef8f]/40 px-2 py-[3px] text-[11px] font-extrabold text-[#38ef8f]">
                    ● LIVE
                  </span>
                ) : src.status === 'upcoming' ? (
                  <span className="hp-mono shrink-0 rounded-[7px] border border-[#7dd3fc]/40 px-2 py-[3px] text-[11px] font-extrabold text-[#7dd3fc]">
                    {dateLabel(src)}
                  </span>
                ) : null}
              </div>
              <div className="flex flex-wrap items-center gap-2 text-[12px] text-[#cbe0f0]/75">
                {src.status !== 'upcoming' && <span>{dateLabel(src)}</span>}
                <span className="text-[#7dd3fc]/60">·</span>
                <span>{src.pool_type}</span>
                {src.day_count > 1 && (
                  <span className="hp-mono rounded-[7px] border border-[#7dd3fc]/40 px-1.5 py-[2px] text-[10px] font-extrabold text-[#7dd3fc]">
                    {src.day_count} days
                  </span>
                )}
              </div>
            </a>
          );
        })}
      </div>
    </section>
  );
}

function Competitions() {
  return (
    <div className="home-page relative min-h-screen overflow-x-clip pb-[96px] text-[#f3f8fd]">
      <div className="hp-shimmer" aria-hidden="true" />

      <AppTopbar active="competitions" />

      <section className="relative px-5 pt-[26px] lg:px-16 lg:pt-[46px]">
        <p className="mb-[18px] text-[11px] font-extrabold uppercase tracking-[0.28em] text-[#7dd3fc] lg:text-[15px] lg:tracking-[0.3em]">
          {`${new Date().getFullYear()} Season · ${HOME_REGION_LABEL}`}
        </p>
        <h1 className="text-[44px] font-black leading-[0.92] tracking-[-0.045em] text-[#f3f8fd] lg:text-[88px] lg:leading-[0.9]">
          Competitions
        </h1>
        <p className="mt-5 max-w-[560px] text-[14.5px] leading-[1.55] text-[#e2f0fc]/[0.82] lg:text-[18px] lg:leading-[1.6]">
          Pick a meet — results are live from the pool.
        </p>
      </section>

      <section
        className="grid grid-cols-1 gap-3 px-4 pt-[26px] sm:grid-cols-2 lg:grid-cols-4 lg:gap-[18px] lg:px-16 lg:pt-12"
        aria-label="Competition pages"
      >
        {PAGES.map((page) => (
          <a
            key={page.href}
            href={page.href}
            className="hp-card-std flex min-h-[130px] flex-col justify-between rounded-[18px] border border-[#7dd3fc]/[0.22] p-[18px] text-inherit no-underline shadow-[0_24px_60px_rgba(2,10,24,0.5)] backdrop-blur-[14px] transition-[transform,border-color,box-shadow] duration-[180ms] ease-out hover:-translate-y-2 hover:border-[#7dd3fc]/80 hover:shadow-[0_28px_60px_rgba(2,10,24,0.65)] focus-visible:-translate-y-2 focus-visible:border-[#7dd3fc]/80 focus-visible:shadow-[0_28px_60px_rgba(2,10,24,0.65)] focus-visible:outline focus-visible:outline-[3px] focus-visible:outline-offset-[3px] focus-visible:outline-[#7dd3fc] lg:min-h-[190px] lg:rounded-[24px] lg:p-[26px]"
          >
            <div className="flex items-start justify-between gap-3">
              <span className="text-[21px] font-black tracking-[-0.02em] lg:text-[26px]">
                {page.title}
              </span>
              <span
                className={`hp-mono mt-[3px] shrink-0 rounded-[7px] border px-2 py-[3px] text-[11px] font-extrabold ${
                  page.live
                    ? 'border-[#38ef8f]/40 text-[#38ef8f]'
                    : 'border-[#7dd3fc]/40 text-[#7dd3fc]'
                }`}
              >
                {page.badge}
              </span>
            </div>
            <div>
              <p className="text-[13px] leading-snug text-[#cbe0f0]/75">{page.subtitle}</p>
              <p className="mt-3 text-[14px] font-extrabold text-[#7dd3fc]">Open results →</p>
            </div>
          </a>
        ))}
      </section>

      <MeetsSection />

      <RecordTicker />
    </div>
  );
}

export default Competitions;
