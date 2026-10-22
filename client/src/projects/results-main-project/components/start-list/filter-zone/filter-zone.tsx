import React, { useCallback, useRef } from 'react';
import { createPortal } from 'react-dom';
import ShareButton from '../share-button';
import ModeSwitch from './mode-switch';
import PeopleRow from './people-row';
import SessionChips, { StuckSummary } from './session-chips';
import SwimmerSearch from './swimmer-search';
import { useStickyZone } from './use-sticky-zone';
import type { FilterZoneClub, FilterZoneMode, FilterZonePerson, FilterZoneSession } from './types';

/**
 * Зона фильтров таба Start list — вариант 5d (десктоп) / 5dm (мобайл),
 * `!design_handoff/FILTER-ZONE-5D`.
 *
 * ГЛАВНОЕ ПРАВИЛО ХЕНДОФФА: обе вкладки — ОДНА И ТА ЖЕ СЕТКА. Меняются тонировка (золото
 * в My plan) и содержимое чипов; ни один элемент не переезжает. Поэтому одна зона с одной
 * разметкой на оба режима, а не два похожих экрана.
 *
 * Три строки: (1) `ModeSwitch` + заголовок-счётчик + Share, (2) `SessionChips` + `SwimmerSearch`,
 * (3) `PeopleRow`. Каждая строка — свой компонент в этой папке (разнесено 22.10.2026); здесь
 * только компоновка, тонировка панели и липкость (`useStickyZone`). Состояние выбора
 * (режим, сессия, состав) живёт выше — в табе и `useStartListSessions`.
 *
 * Что сюда сознательно НЕ въехало: «Updated · Refresh» (это про свежесть данных зума,
 * а не про фильтр) и футер «времена приблизительные».
 */
export default function FilterZone({
  mode, onModeChange, planSwims, sessions, activeSessionKey, onSelectSession, shareUrl,
  orgCompIds, onOpenSwimmer, planSwimmerIds, favSwimmers, favClubs, planSwimmers, planClubs,
  onRemoveSwimmer, onRemoveClub, onEditPlan,
}: {
  mode: FilterZoneMode;
  onModeChange: (mode: FilterZoneMode) => void;
  /** N в «My plan · N» — сколько всего моих заплывов на старте. 0 — без числа. */
  planSwims: number;

  sessions: FilterZoneSession[];
  /** Ключ открытой сессии (`sessionKey`) — общий выбор для All и My plan. */
  activeSessionKey: string;
  onSelectSession: (key: string) => void;

  shareUrl: string;

  orgCompIds: number[];
  onOpenSwimmer: (swimmerId: number) => void;
  planSwimmerIds: ReadonlySet<number>;

  /** Строка 3, режим All: избранные пловцы и клубы. Пусто — строки нет вовсе. */
  favSwimmers: FilterZonePerson[];
  favClubs: FilterZoneClub[];
  /** Строка 3, режим My plan: состав. */
  planSwimmers: FilterZonePerson[];
  planClubs: FilterZoneClub[];
  onRemoveSwimmer: (id: number) => void;
  onRemoveClub: (id: number) => void;
  onEditPlan: () => void;
}) {
  const plan = mode === 'plan';
  const rootRef = useRef<HTMLDivElement>(null);
  const { stuck, sentinelRef, top } = useStickyZone();

  // Открыть пловца — из выдачи поиска или чипа избранного. Карточка рисуется НИЖЕ зоны,
  // поэтому подтягиваем зону к верху: иначе на телефоне тап выглядит несработавшим.
  const pick = useCallback((swimmerId: number) => {
    onOpenSwimmer(swimmerId);
    requestAnimationFrame(() => rootRef.current?.scrollIntoView({ block: 'start' }));
  }, [onOpenSwimmer]);

  const swimmerChips = plan ? planSwimmers : favSwimmers;
  const clubChips = plan ? planClubs : favClubs;
  // Гостю без фаворитов строка 3 в All не рендерится — панель остаётся в две строки.
  const showPeople = plan || swimmerChips.length > 0 || clubChips.length > 0;

  const activeSession = sessions.find((s) => s.key === activeSessionKey) ?? sessions[0];

  // В All считаем ЗАЯВКИ сессии, в плане — СВОИ ЗАПЛЫВЫ, и слово меняется вместе с числом:
  // «39 entries» про свой план читалось бы как «в сессии 39 заявок».
  const title = (() => {
    if (!activeSession) return null;
    const n = plan ? activeSession.mine : activeSession.entries;
    const noun = plan ? (n === 1 ? 'swim' : 'swims') : (n === 1 ? 'entry' : 'entries');
    return `${n} ${noun}${activeSession.sourceName ? ` · ${activeSession.sourceName}` : ''}`;
  })();

  return (
    <>
    {/* Сентинел: пустой блок в обычном потоке ровно там, где зона стоит НЕ прилипнув. */}
    <div ref={sentinelRef} aria-hidden className="h-0" />
    {/* ЛИПКАЯ на всех ширинах (решение Влада 31.08.2026): переключиться между программой и
        планом, сменить сессию или найти своего нужно с любого места длинного протокола.
        Прилипает ПОД липкой шапкой страницы (`top` из `useStickyZone`). Липнет
        ЗОНА ЦЕЛИКОМ: `sticky`-ребёнок не выходит за пределы родителя. Фон обязателен —
        сквозь скруглённые углы просвечивал бы уезжающий протокол. */}
    <div
      className="sticky z-30 mb-3 pb-2 pt-2"
      style={{ top, background: 'var(--theme-mode-page-bg)' }}
      ref={rootRef}
    >
      <div
        className="rounded-[14px] border px-3 py-2.5 sm:px-3.5"
        style={{
          background: plan ? 'var(--theme-personal-bg)' : 'var(--deep-card-bg)',
          borderColor: plan ? 'var(--theme-personal-border)' : 'var(--deep-card-border)',
        }}
      >
        {/* ── Строка 1: режим · заголовок · Share ── */}
        <div className="flex items-center gap-2.5">
          <ModeSwitch mode={mode} planSwims={planSwims} onChange={onModeChange} />

          {/* Заголовок-счётчик — ТОЛЬКО от 640px: на телефоне имя протокола всё равно
              обрезается, а число видно на чипе сессии (решение Влада 31.08.2026). */}
          {title && (
            <span
              dir="auto"
              className="hidden min-w-0 flex-1 overflow-hidden text-ellipsis whitespace-nowrap text-right text-[12px] font-bold sm:block"
              style={{ color: 'var(--deep-text-mute)' }}
            >
              {title}
            </span>
          )}

          <ShareButton url={shareUrl} />
        </div>

        {/* ── Строка 2: сессии + поиск. Поиск живой и в прилипшем виде — искать своего
            нужно как раз в середине длинного протокола. ── */}
        <div className="mt-2 flex flex-col gap-2 sm:flex-row sm:items-center">
          {stuck ? (
            <StuckSummary
              mode={mode}
              session={activeSession}
              peopleNames={[...swimmerChips.map((p) => p.name), ...clubChips.map((c) => c.name)]}
            />
          ) : (
            <SessionChips mode={mode} sessions={sessions} activeKey={activeSessionKey} onSelect={onSelectSession} />
          )}
          <SwimmerSearch mode={mode} orgCompIds={orgCompIds} planSwimmerIds={planSwimmerIds} onPick={pick} />
        </div>

        {/* ── Строка 3: люди. В прилипшем виде её нет — имена ушли в строку 2 текстом. ── */}
        {!stuck && showPeople && (
          <PeopleRow
            mode={mode}
            swimmers={swimmerChips}
            clubs={clubChips}
            onPickSwimmer={pick}
            onRemoveSwimmer={onRemoveSwimmer}
            onRemoveClub={onRemoveClub}
            onModeChange={onModeChange}
            onEditPlan={onEditPlan}
          />
        )}
      </div>
    </div>

    {/* Кнопка «наверх» — пока шапка погашена, это короткий путь обратно к табам. Портал в
        body: внутри липкой зоны кнопка ездила бы вместе с ней. Место — левее переключателя
        темы (`UI_ModeToggle`, bottom-4 right-4, 40px). Токены СТРАНИЧНЫЕ, а не `--deep-*`:
        из портала токены темы таба не видны. */}
    {stuck && createPortal(
      <button
        type="button"
        onClick={() => window.scrollTo({ top: 0, behavior: 'smooth' })}
        aria-label="Back to top"
        title="Back to top"
        className="fixed bottom-4 right-[68px] z-[110] flex h-11 w-11 items-center justify-center rounded-full shadow-lg"
        style={{ background: 'var(--theme-primary)', color: 'var(--theme-mode-accent-text)' }}
      >
        <svg
          width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor"
          strokeWidth={2.5} strokeLinecap="round" strokeLinejoin="round" aria-hidden
        >
          <path d="M12 19V5M5 12l7-7 7 7" />
        </svg>
      </button>,
      document.body,
    )}
    </>
  );
}
