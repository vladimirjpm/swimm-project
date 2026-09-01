import React, { useMemo, useState } from 'react';
import { useAppSelector } from '../../../../store/store';
import { useClubSummary } from '../../../../hooks/useClubSummary';
import { routes } from '../../../../utils/routes';
import type { Result } from '../../../../utils/interfaces/results';

// Таб Clubs: клубный зачёт источника (/api/club-summary, фаза 3.4) + drill-down по клубу:
// клик по строке → панель клуба справа (пловцы + разбор очков из загруженных результатов)
// + «Open in Swims →». Выбор пишется в ?club=<имя> (НАВ-контракт задумывал clubId, но у
// зачёта нет стабильного id — ключ клуба текстовый, включая эстафетные команды).

const cardStyle: React.CSSProperties = {
  background: 'var(--theme-mode-surface)',
  color: 'var(--theme-mode-text)',
  boxShadow: 'var(--theme-mode-card-shadow)',
  border: '1px solid var(--theme-mode-card-border)',
};

// Панель клуба несёт ту же подсветку, что и выбранная строка зачёта: слева выбрано —
// справа его содержимое. Тон тот же (--theme-primary 8%), но по непрозрачной подложке,
// иначе на тёмной теме поверхность просвечивает и связь со строкой теряется.
const selectedCardStyle: React.CSSProperties = {
  background: 'color-mix(in srgb, var(--theme-primary) 8%, var(--theme-mode-surface))',
  color: 'var(--theme-mode-text)',
  boxShadow: 'var(--theme-mode-card-shadow)',
  border: '1px solid color-mix(in srgb, var(--theme-primary) 35%, var(--theme-mode-card-border))',
};

const ROW_SELECTED_BG = 'color-mix(in srgb, var(--theme-primary) 8%, transparent)';

interface Props {
  sourceParams?: Record<string, string>;
  /** Переход в Swims с фильтром по клубу. */
  onOpenSwimsForClub?(club: string): void;
}

function writeClubUrl(club: string | null) {
  const url = new URL(window.location.href);
  if (club) url.searchParams.set('club', club);
  else url.searchParams.delete('club');
  window.history.replaceState(null, '', url.toString());
}

/** Ключ клуба у строки результата — как в зачёте (сервер: club → relay_team_name → club_en).
 *  Эстафеты обязаны попадать в тот же клуб, что и личные заплывы. */
function clubKeyOf(r: Result): string | null {
  return r.club?.trim() || r.relay_team_name?.trim() || r.club_en?.trim() || null;
}

/** Имя в разборе: у эстафеты — команда (состав ног не показываем), у личного — пловец. */
function entrantOf(r: Result): string {
  if (r.is_relay) return r.relay_team_name?.trim() || 'Relay team';
  return `${r.first_name ?? ''} ${r.last_name ?? ''}`.trim() || '—';
}

/** Дисциплина строки: «100 freestyle», у эстафеты дистанция вида «4X50». */
function eventOf(r: Result): string {
  return `${r.event_style_len ?? ''} ${r.event_style_name ?? ''}`.trim() || r.event || '—';
}

export default function CompetitionClubs({ sourceParams, onOpenSwimsForClub }: Props) {
  const clubs = useClubSummary(sourceParams, !!sourceParams);
  const selectedSource = useAppSelector((s) => s.dataSourceSelected);
  // Тоггл «Combine All Results»: он же управляет зачётом слева, поэтому разбор справа
  // обязан брать ту же пару «место + очки», иначе панели противоречат друг другу.
  const isCombined = useAppSelector((s) => !!s.filterSelected.is_recalculated);
  const [selectedClub, setSelectedClub] = useState<string | null>(
    () => new URLSearchParams(window.location.search).get('club'),
  );
  const [panelTab, setPanelTab] = useState<'swimmers' | 'points'>('swimmers');

  const selectClub = (club: string | null) => {
    setSelectedClub(club);
    writeClubUrl(club);
  };

  // Заплывы выбранного клуба из загруженных результатов источника (в paged-режиме это
  // может быть не весь протокол — отсюда сноска под разбором очков).
  const clubRows = useMemo<Result[]>(() => {
    if (!selectedClub) return [];
    return (selectedSource?.results ?? []).filter((r: Result) => clubKeyOf(r) === selectedClub);
  }, [selectedClub, selectedSource]);

  /** Место с учётом тоггла: объединённое место дисциплины либо протокольное. */
  const placeOf = (r: Result): number | null =>
    (isCombined ? r.combined_place ?? r.position : r.position) ?? null;

  /** Очки за заплыв — считает сервер (Э6), клиент только выбирает поле по тогглу. */
  const pointsOf = (r: Result): number =>
    (isCombined ? r.combined_club_points ?? r.club_points : r.club_points) ?? 0;

  const clubSwimmers = useMemo(() => {
    // ⚠ У эстафеты «участник» — это КОМАНДА, а имя команды равно названию клуба, поэтому
    // все эстафеты клуба схлопываются в одну строку с названием клуба. Без пометки она
    // читается как пловец-фантом («кто этот пловец с именем клуба на 8 заплывов?»).
    // Бейдж «relay» тут такой же, как на вкладке Points.
    const bySwimmer = new Map<string, { name: string; swims: number; gold: number; silver: number; bronze: number; bestPts: number; isRelay: boolean }>();
    for (const r of clubRows) {
      const name = entrantOf(r);
      const e = bySwimmer.get(name) ?? { name, swims: 0, gold: 0, silver: 0, bronze: 0, bestPts: 0, isRelay: false };
      e.swims += 1;
      if (r.is_relay) e.isRelay = true;
      const pos = placeOf(r);
      if (pos === 1) e.gold += 1;
      else if (pos === 2) e.silver += 1;
      else if (pos === 3) e.bronze += 1;
      e.bestPts = Math.max(e.bestPts, r.international_points ?? 0);
      bySwimmer.set(name, e);
    }
    return [...bySwimmer.values()].sort(
      (a, b) => b.gold - a.gold || b.silver - a.silver || b.bronze - a.bronze || b.bestPts - a.bestPts,
    );
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [clubRows, isCombined]);

  // Разбор очков: только очковые заплывы (остальные в сумму не входят — ровно они и есть
  // «scoring swims» в зачёте). Эстафеты идут строкой команды, без состава ног: очки
  // эстафеты уже с множителем правила (обычно ×2) и принадлежат команде, а не ноге.
  const pointRows = useMemo(() => {
    return clubRows
      .map((r, i) => ({
        key: r.id ?? `${entrantOf(r)}|${eventOf(r)}|${i}`,
        entrant: entrantOf(r),
        event: eventOf(r),
        place: placeOf(r),
        points: pointsOf(r),
        isRelay: !!r.is_relay,
      }))
      .filter((r) => r.points > 0)
      .sort((a, b) => b.points - a.points || (a.place ?? 999) - (b.place ?? 999));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [clubRows, isCombined]);

  const pointsTotal = pointRows.reduce((sum, r) => sum + r.points, 0);
  const selectedSummary = selectedClub ? clubs.find((c) => c.club === selectedClub) : null;
  // Id клуба для ссылки на его страницу: сперва из зачёта (сервер отдаёт clubId), затем из
  // загруженных строк. Второй путь нужен эстафетным командам и старым источникам, где
  // ключ зачёта — имя, а id приходит только со строкой результата. Нет ни там, ни там
  // (псевдоклуб/статический источник) — ссылки просто нет, панель остаётся прежней.
  const selectedClubId = selectedSummary?.clubId || clubRows.find((r) => r.club_id)?.club_id || null;
  // Загруженных строк меньше, чем в зачёте (paged-режим / фильтр) — сумма разбора не сойдётся
  // с рейтингом клуба. Молча расходиться нельзя: это ровно тот случай, когда цифру идут проверять.
  const partial = !!selectedSummary && pointsTotal !== selectedSummary.points;

  return (
    // Две колонки 50/50: слева полный зачёт, справа панель выбранного клуба. Без выбора
    // зачёт занимает всю ширину. На узком экране колонки схлопываются в одну, и панель
    // клуба идёт ПЕРВОЙ (order-first) — это ответ на только что сделанный тап по строке.
    <div className={`mt-4 grid gap-3 ${selectedClub ? 'lg:grid-cols-2 lg:items-start' : ''}`}>
      {/* Полный зачёт */}
      <div className="min-w-0 overflow-x-auto rounded-[12px] p-4" style={cardStyle}>
        <div className="mb-2 text-[14px] font-extrabold">Club standings</div>
        {clubs.length === 0 ? (
          <div className="py-6 text-center text-[12.5px] font-semibold" style={{ color: 'var(--theme-mode-text-muted)' }}>
            No club data for this competition.
          </div>
        ) : (
          <table className="w-full border-collapse text-[12.5px]">
            <thead>
              <tr className="text-left text-[10px] font-extrabold uppercase tracking-wide" style={{ color: 'var(--theme-mode-text-muted)' }}>
                <th className="py-1 pr-2">#</th>
                <th className="py-1 pr-2">Club</th>
                <th className="py-1 pr-2">Swimmers</th>
                <th className="py-1 pr-2">Medals</th>
                <th className="py-1 text-right">Rating</th>
              </tr>
            </thead>
            <tbody>
              {clubs.map((c, i) => (
                <tr
                  key={c.club}
                  className={`cursor-pointer border-t ${selectedClub === c.club ? 'font-extrabold' : ''}`}
                  style={{
                    borderColor: 'var(--theme-mode-border)',
                    background: selectedClub === c.club ? ROW_SELECTED_BG : undefined,
                  }}
                  onClick={() => selectClub(selectedClub === c.club ? null : c.club)}
                >
                  <td className="py-1.5 pr-2 font-bold" style={{ color: 'var(--theme-mode-text-muted)' }}>{i + 1}</td>
                  <td className="py-1.5 pr-2 font-bold" dir="auto">{c.club}</td>
                  <td className="py-1.5 pr-2">{c.swimmerCount}</td>
                  <td className="py-1.5 pr-2 whitespace-nowrap">🥇{c.gold} 🥈{c.silver} 🥉{c.bronze}</td>
                  <td className="py-1.5 text-right font-extrabold" style={{ color: 'var(--theme-primary)' }}>{c.points}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>

      {/* Панель выбранного клуба (drill-down) — правая колонка, липнет при скролле
          длинного зачёта (59 клубов на событии). */}
      {selectedClub && (
        <div
          className="order-first min-w-0 rounded-[12px] p-4 lg:order-none lg:sticky lg:top-4"
          style={selectedCardStyle}
        >
          <div className="mb-2 flex flex-wrap items-center justify-between gap-2">
            <div className="min-w-0 text-[15px] font-extrabold" dir="auto">
              {selectedClub}
              {selectedSummary && (
                <span className="ml-2 whitespace-nowrap text-[12.5px] font-bold" style={{ color: 'var(--theme-mode-text-secondary)' }}>
                  🥇{selectedSummary.gold} 🥈{selectedSummary.silver} 🥉{selectedSummary.bronze} · {selectedSummary.points} pts
                </span>
              )}
            </div>
            <span className="flex items-center gap-2">
              {onOpenSwimsForClub && (
                <button
                  type="button"
                  onClick={() => onOpenSwimsForClub(selectedClub)}
                  className="cursor-pointer bg-transparent p-0 text-[12px] font-extrabold hover:underline"
                  style={{ color: 'var(--theme-primary)' }}
                >
                  Open in Swims →
                </button>
              )}
              {selectedClubId && (
                <a
                  href={routes.club(selectedClubId)}
                  className="text-[12px] font-extrabold hover:underline"
                  style={{ color: 'var(--theme-primary)' }}
                >
                  Open in club page →
                </a>
              )}
              <button
                type="button"
                onClick={() => selectClub(null)}
                className="cursor-pointer bg-transparent p-0 text-[13px]"
                style={{ color: 'var(--theme-mode-text-muted)' }}
                aria-label="Close club details"
              >
                ✕
              </button>
            </span>
          </div>

          {/* Табы панели: кто плыл / из чего сложился рейтинг */}
          <div className="mb-2 flex gap-1.5">
            {([['swimmers', 'Swimmers'], ['points', 'Points']] as const).map(([id, label]) => (
              <button
                key={id}
                type="button"
                onClick={() => setPanelTab(id)}
                className="cursor-pointer rounded-full px-2.5 py-[3px] text-[11.5px] font-extrabold"
                style={panelTab === id
                  ? { background: 'var(--theme-primary)', color: 'var(--theme-mode-accent-text)' }
                  : { background: 'transparent', color: 'var(--theme-mode-text-muted)', border: '1px solid var(--theme-mode-border)' }}
              >
                {label}
              </button>
            ))}
          </div>

          {clubRows.length === 0 ? (
            <div className="py-3 text-[12.5px] font-semibold" style={{ color: 'var(--theme-mode-text-muted)' }}>
              Swimmer details appear when results are loaded.
            </div>
          ) : panelTab === 'swimmers' ? (
            <div className="overflow-x-auto">
              <table className="w-full border-collapse text-[12.5px]">
                <thead>
                  <tr className="text-left text-[10px] font-extrabold uppercase tracking-wide" style={{ color: 'var(--theme-mode-text-muted)' }}>
                    <th className="py-1 pr-2">Swimmer</th>
                    <th className="py-1 pr-2">Swims</th>
                    <th className="py-1 pr-2">Medals</th>
                    <th className="py-1 text-right">Best pts</th>
                  </tr>
                </thead>
                <tbody>
                  {clubSwimmers.map((s) => (
                    <tr key={s.name} className="border-t" style={{ borderColor: 'var(--theme-mode-border)' }}>
                      <td className="py-1.5 pr-2 font-bold" dir="auto">
                        {s.name}
                        {s.isRelay && (
                          <span className="ml-1.5 rounded-full px-1.5 py-px text-[9.5px] font-extrabold uppercase tracking-wide"
                            style={{ background: ROW_SELECTED_BG, color: 'var(--theme-primary)' }}>
                            relay
                          </span>
                        )}
                      </td>
                      <td className="py-1.5 pr-2">{s.swims}</td>
                      <td className="py-1.5 pr-2 whitespace-nowrap">
                        {s.gold > 0 && `🥇${s.gold} `}
                        {s.silver > 0 && `🥈${s.silver} `}
                        {s.bronze > 0 && `🥉${s.bronze}`}
                      </td>
                      <td className="py-1.5 text-right font-extrabold" style={{ color: 'var(--theme-primary)' }}>
                        {s.bestPts > 0 ? s.bestPts : ''}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          ) : pointRows.length === 0 ? (
            <div className="py-3 text-[12.5px] font-semibold" style={{ color: 'var(--theme-mode-text-muted)' }}>
              No scoring swims in the loaded results.
            </div>
          ) : (
            <div className="overflow-x-auto">
              <table className="w-full border-collapse text-[12.5px]">
                <thead>
                  <tr className="text-left text-[10px] font-extrabold uppercase tracking-wide" style={{ color: 'var(--theme-mode-text-muted)' }}>
                    <th className="py-1 pr-2">Swimmer</th>
                    <th className="py-1 pr-2">Event</th>
                    <th className="py-1 pr-2 text-right">Place</th>
                    <th className="py-1 text-right">Pts</th>
                  </tr>
                </thead>
                <tbody>
                  {pointRows.map((r) => (
                    <tr key={r.key} className="border-t" style={{ borderColor: 'var(--theme-mode-border)' }}>
                      <td className="py-1.5 pr-2 font-bold" dir="auto">
                        {r.entrant}
                        {r.isRelay && (
                          <span className="ml-1.5 rounded-full px-1.5 py-px text-[9.5px] font-extrabold uppercase tracking-wide"
                            style={{ background: ROW_SELECTED_BG, color: 'var(--theme-primary)' }}>
                            relay
                          </span>
                        )}
                      </td>
                      <td className="py-1.5 pr-2 whitespace-nowrap">{r.event}</td>
                      <td className="py-1.5 pr-2 text-right font-bold">{r.place ?? '—'}</td>
                      <td className="py-1.5 text-right font-extrabold" style={{ color: 'var(--theme-primary)' }}>{r.points}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
              <div
                className="mt-2 flex items-baseline justify-between gap-2 border-t pt-2 text-[12px] font-extrabold"
                style={{ borderColor: 'var(--theme-mode-border)' }}
              >
                <span>{pointRows.length} scoring swims{isCombined ? ' · combined places' : ''}</span>
                <span style={{ color: 'var(--theme-primary)' }}>{pointsTotal} pts</span>
              </div>
              {partial && (
                <div className="mt-1 text-[11px] font-semibold" style={{ color: 'var(--theme-mode-text-muted)' }}>
                  Standings show {selectedSummary!.points} pts — the breakdown covers only the swims loaded on this page.
                </div>
              )}
            </div>
          )}
        </div>
      )}
    </div>
  );
}
