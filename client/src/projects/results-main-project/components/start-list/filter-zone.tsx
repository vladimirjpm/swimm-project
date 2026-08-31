import React, { useCallback, useEffect, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import UI_ClubIcon from '../../../components/mix/club-icon/club-icon';
import { setStickyBarSuppressed } from '../competition-header/sticky-chrome';
import ShareButton from './share-button';
import { useStartListSearch } from './use-start-list';
import { dayLabel, formatApproxTime, sessionLabel } from './start-list-helpers';

/**
 * Зона фильтров таба Start list — вариант 5d (десктоп) / 5dm (мобайл),
 * `!design_handoff/FILTER-ZONE-5D`.
 *
 * ГЛАВНОЕ ПРАВИЛО ХЕНДОФФА: обе вкладки — ОДНА И ТА ЖЕ СЕТКА. Меняются тонировка (золото
 * в My plan) и содержимое чипов; ни один элемент не переезжает. Поэтому здесь один
 * компонент с одной разметкой на оба режима, а не два похожих экрана.
 *
 * Три строки: (1) сегмент-контрол + заголовок-счётчик + Share, (2) сессии + поиск,
 * (3) люди. До 5d это были пять разных блоков вокруг зумов — переключатель-плитки,
 * отдельная строка Share, панель «найти своего» с чипами избранных, подтабы источников
 * (`source-tabs.tsx`) и дни-чипы внутри карточки плана. Зона занимала ~400px, теперь ~150.
 *
 * Что сюда сознательно НЕ въехало: «Updated · Refresh» (это про свежесть данных зума,
 * а не про фильтр) и футер «времена приблизительные».
 */

/** Сессия = один протокол федерации. У окружных чемпионатов их несколько на один старт. */
export interface FilterZoneSession {
  orgCompId: number;
  index: number;
  /** dd/MM; null — даты у привязки нет. */
  date: string | null;
  /** yyyy-MM-dd — из неё берётся день недели. */
  dateIso: string | null;
  /** Имя протокола у федерации — идёт в заголовок-счётчик активной сессии. */
  sourceName: string | null;
  /** Заявок в сессии — счётчик чипа в режиме All. */
  entries: number;
  /** Моих заплывов в сессии — счётчик чипа в режиме My plan. */
  mine: number;
}

export interface FilterZonePerson {
  id: number;
  name: string;
  /** Пловец-фаворит из профиля: в All он золотой, обычный — серый. */
  favorite?: boolean;
}

export interface FilterZoneClub {
  id: number;
  name: string;
  swimmers: number;
}

interface Props {
  mode: 'all' | 'plan';
  onModeChange: (mode: 'all' | 'plan') => void;
  /** N в «My plan · N» — сколько всего моих заплывов на старте. 0 — без числа. */
  planSwims: number;

  sessions: FilterZoneSession[];
  activeOrgCompId: number;
  onSelectSession: (orgCompId: number) => void;

  shareUrl: string;

  /** Поиск идёт по ВСЕМ источникам сразу: родителю всё равно, в каком протоколе его ребёнок. */
  orgCompIds: number[];
  onOpenSwimmer: (swimmerId: number) => void;
  /** В режиме плана выдача сужается до этих пловцов — поле обещает поиск «в моём плане». */
  planSwimmerIds: ReadonlySet<number>;

  /** Строка 3, режим All: избранные пловцы и клубы. Пусто — строки нет вовсе. */
  favSwimmers: FilterZonePerson[];
  favClubs: FilterZoneClub[];
  /** Строка 3, режим My plan: состав. */
  planSwimmers: FilterZonePerson[];
  planClubs: FilterZoneClub[];
  onRemoveSwimmer: (id: number) => void;
  onRemoveClub: (id: number) => void;
  /** «Add swimmer / club» и «Edit» — оба ведут в пикер состава. */
  onEditPlan: () => void;
}

export default function FilterZone({
  mode, onModeChange, planSwims, sessions, activeOrgCompId, onSelectSession, shareUrl,
  orgCompIds, onOpenSwimmer, planSwimmerIds, favSwimmers, favClubs, planSwimmers, planClubs,
  onRemoveSwimmer, onRemoveClub, onEditPlan,
}: Props) {
  const plan = mode === 'plan';
  const [query, setQuery] = useState('');
  const { data: rawHits, loading } = useStartListSearch(orgCompIds, query);
  const rootRef = useRef<HTMLDivElement>(null);
  const inputRef = useRef<HTMLInputElement>(null);

  // В плане ищем среди своих: поле обещает «in my plan», и общая выдача по старту врала бы.
  const hits = plan && rawHits ? rawHits.filter((h) => planSwimmerIds.has(h.swimmer_id)) : rawHits;

  /**
   * Выбор пловца из выдачи. Выдачу ОБЯЗАТЕЛЬНО схлопываем (баг 31.08.2026): карточка
   * пловца рисуется НИЖЕ зоны, а раскрытый список закрывал её на весь экран — на телефоне
   * выглядело так, будто тап не сработал. Заодно снимаем фокус, иначе клавиатура закрывает
   * то, ради чего человек и нажимал.
   */
  const pick = useCallback((swimmerId: number) => {
    setQuery('');
    inputRef.current?.blur();
    onOpenSwimmer(swimmerId);
    requestAnimationFrame(() => rootRef.current?.scrollIntoView({ block: 'start' }));
  }, [onOpenSwimmer]);

  const swimmerChips = plan ? planSwimmers : favSwimmers;
  const clubChips = plan ? planClubs : favClubs;
  // Гостю без фаворитов строка 3 в All не рендерится — панель остаётся в две строки.
  const showPeople = plan || swimmerChips.length > 0 || clubChips.length > 0;

  /**
   * Прилипла ли зона. Отсюда три следствия сразу (решения Влада 31.08.2026):
   *
   * 1. чипы сессий и строка людей сворачиваются в ОДНУ текстовую строку — пока зона стоит
   *    на своём месте, это органы управления, а когда висит поверх протокола, это справка
   *    «что сейчас показано», и трогать её незачем;
   * 2. компакт-бар шапки соревнования гаснет совсем (`setStickyBarSuppressed`) — двум
   *    липким панелям друг на друге на телефоне места нет;
   * 3. внизу появляется кнопка «↑» — единственный способ вернуться наверх коротко, раз
   *    шапки со сменой таба на экране больше нет.
   *
   * Меряем по СЕНТИНЕЛУ — пустому блоку в обычном потоке прямо перед зоной, — а не по самой
   * зоне: у прилипшей зоны `rect.top` навсегда равен смещению прилипания, условие «дошла
   * до верха» осталось бы истинным вечно и она никогда не отлипла бы.
   *
   * Порог — высота топбара, а не `--comp-sticky-chrome-h`: сама переменная зависит от того,
   * виден ли бар, а бар мы этим же флагом и гасим. Считать одно через другое — это качели
   * «прилипла → бар исчез → порог уехал → отлипла → бар вернулся» на каждом кадре.
   */
  const [stuck, setStuck] = useState(false);
  const sentinelRef = useRef<HTMLDivElement>(null);
  useEffect(() => {
    const onScroll = () => {
      const sentinel = sentinelRef.current;
      if (!sentinel) return;
      const topbar = document.querySelector('[data-app-topbar]');
      const topbarH = topbar?.getBoundingClientRect().height ?? 0;
      // +1px — запас на дробные пиксели зума: ровно на границе рект даёт 45.99.
      setStuck(sentinel.getBoundingClientRect().top <= topbarH + 1);
    };
    onScroll();
    window.addEventListener('scroll', onScroll, { passive: true });
    window.addEventListener('resize', onScroll);
    return () => {
      window.removeEventListener('scroll', onScroll);
      window.removeEventListener('resize', onScroll);
    };
  }, []);

  // Гасим шапку, пока зона прилипла. Снимать ОБЯЗАТЕЛЬНО в cleanup: уход с таба (или
  // экран «протокол не опубликован», где зоны нет вовсе) не должен оставить шапку
  // выключенной на всей странице.
  useEffect(() => {
    setStickyBarSuppressed(stuck);
    return () => setStickyBarSuppressed(false);
  }, [stuck]);

  const activeSession = sessions.find((s) => s.orgCompId === activeOrgCompId) ?? sessions[0];
  const peopleNames = [...swimmerChips.map((p) => p.name), ...clubChips.map((c) => c.name)];

  const title = (() => {
    const active = sessions.find((s) => s.orgCompId === activeOrgCompId) ?? sessions[0];
    if (!active) return null;
    // В All считаем ЗАЯВКИ сессии, в плане — СВОИ ЗАПЛЫВЫ, и слово меняется вместе с
    // числом: «39 entries» про свой план читалось бы как «в сессии 39 заявок».
    const n = plan ? active.mine : active.entries;
    const noun = plan ? (n === 1 ? 'swim' : 'swims') : (n === 1 ? 'entry' : 'entries');
    return `${n} ${noun}${active.sourceName ? ` · ${active.sourceName}` : ''}`;
  })();


  return (
    <>
    {/* Сентинел: пустой блок в обычном потоке ровно там, где зона стоит НЕ прилипнув.
        По нему и считается «дошла до верха» — см. комментарий у `stuck`. */}
    <div ref={sentinelRef} aria-hidden className="h-0" />
    {/* ЛИПКАЯ на всех ширинах (решение Влада 31.08.2026): переключиться между программой
        и своим планом, сменить сессию или найти своего нужно с любого места длинного
        протокола, а не только с его начала. Прилипает ПОД липкой шапкой страницы — её
        высоту публикует компакт-бар соревнования в `--comp-sticky-chrome-h` (свою, когда
        выехал, иначе высоту топбара), см. хендофф 16b. Прилипнув, зона бар и гасит, так
        что в этом состоянии переменная равна высоте топбара.

        Липнет ЗОНА ЦЕЛИКОМ, а не одна её строка: `sticky`-ребёнок не выходит за пределы
        родителя и уехал бы вместе с панелью. Отсюда же требование к высоте — 5d ужал зону
        с ~400px до ~105 на десктопе и ~150–190 на телефоне, и только поэтому её вообще
        можно держать на экране.

        Фон и отрицательные поля обязательны: панель скруглена, и без подложки во всю
        ширину контейнера сквозь её углы и зазоры просвечивал бы уезжающий протокол. */}
    <div
      className="sticky z-30 -mx-4 mb-3 px-4 pb-2 pt-2 lg:-mx-6 lg:px-6"
      style={{ top: 'var(--comp-sticky-chrome-h, 0px)', background: 'var(--theme-mode-page-bg)' }}
      ref={rootRef}
    >
      <div
        className="rounded-[14px] border px-3 py-2.5 sm:px-3.5"
        style={{
          background: plan ? 'var(--theme-personal-bg)' : 'var(--deep-card-bg)',
          borderColor: plan ? 'var(--theme-personal-border)' : 'var(--deep-card-border)',
        }}
      >
        {/* ── Строка 1: режим · заголовок · Share ───────────────────────────────────── */}
        <div className="flex items-center gap-2.5">
          <div
            className="flex min-w-0 flex-1 gap-0.5 rounded-[12px] p-[3px] sm:flex-none"
            style={{ background: 'var(--deep-seg-track)' }}
            role="tablist"
            aria-label="Start list view"
          >
            <ModeSegment
              active={!plan}
              onClick={() => onModeChange('all')}
              label="All programme"
              color="var(--deep-accent)"
            />
            <ModeSegment
              active={plan}
              onClick={() => onModeChange('plan')}
              label={planSwims > 0 ? `⭐ My plan · ${planSwims}` : '⭐ My plan'}
              color="var(--theme-personal-accent)"
            />
          </div>

          {/* Заголовок-счётчик — ТОЛЬКО от 640px. В 5dm он стоял тонкой строкой внизу
              зоны, но на телефоне это лишняя строка: имя протокола там всё равно
              обрезается многоточием, а сколько заявок — видно на самом чипе сессии
              (решение Влада 31.08.2026). */}
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

        {/* ── Строка 2: сессии + поиск ──────────────────────────────────────────────── */}
        <div className="mt-2 flex flex-col gap-2 sm:flex-row sm:items-center">
          {/* Прилипшая зона: одна строка ТЕКСТОМ на месте чипов — выбранная сессия и кто
              в составе. Оформление то же (цвет активного чипа, метка состава), но это
              спаны, а не кнопки: ни одного действия. Поиск остаётся живым — искать своего
              нужно как раз в середине длинного протокола. */}
          {stuck ? (
            <div className="flex min-w-0 flex-1 items-center gap-2 overflow-hidden whitespace-nowrap py-[3px]">
              {activeSession && (
                <span
                  className="flex-none text-[12.5px] font-extrabold"
                  style={{ color: plan ? 'var(--theme-personal-accent)' : 'var(--deep-accent)' }}
                >
                  {sessionLabel(activeSession.dateIso, activeSession.date, activeSession.index)}
                  <span className="ml-1.5 font-bold opacity-75">
                    {plan ? activeSession.mine : activeSession.entries}
                  </span>
                </span>
              )}
              {peopleNames.length > 0 && (
                <>
                  <span className="flex-none opacity-30" aria-hidden>·</span>
                  <span
                    className="flex-none text-[10px] font-black uppercase tracking-wide"
                    style={{ color: plan ? 'var(--theme-personal-accent)' : 'var(--deep-text-mute)' }}
                  >
                    {plan ? '⭐ Plan' : '❤️ Favorites'}
                  </span>
                  <span
                    dir="auto"
                    className="min-w-0 truncate text-[12px] font-bold"
                    style={{ color: 'var(--deep-text-mute)' }}
                  >
                    {peopleNames.join(' · ')}
                  </span>
                </>
              )}
            </div>
          ) : sessions.length > 0 && (
            // Мобайл: скролл край-в-край (отрицательные поля гасят паддинг панели), чтобы
            // внутри рамки не стояла полоса обрезанных чипов.
            <div className="-mx-3 flex gap-1.5 overflow-x-auto px-3 sm:mx-0 sm:flex-wrap sm:px-0">
              {sessions.map((s) => {
                const active = s.orgCompId === activeOrgCompId;
                const count = plan ? s.mine : s.entries;
                // Сессия без моих заплывов в плане некликабельна: открывать там нечего.
                const dead = plan && s.mine === 0;
                return (
                  <button
                    key={s.orgCompId}
                    type="button"
                    disabled={dead}
                    title={s.sourceName ?? undefined}
                    onClick={() => onSelectSession(s.orgCompId)}
                    className="flex-none whitespace-nowrap rounded-full px-3 py-[7px] text-[12.5px] font-extrabold"
                    style={{
                      border: `${active ? 1.5 : 1}px solid ${
                        dead || !active
                          ? 'var(--deep-card-border)'
                          : plan ? 'var(--theme-personal-border)' : 'var(--deep-accent)'
                      }`,
                      background: !active ? 'transparent'
                        : plan ? 'var(--theme-personal-badge-bg)' : 'var(--deep-accent-chip)',
                      color: dead ? 'var(--deep-text-faint)'
                        : plan ? 'var(--theme-personal-accent)' : 'var(--deep-accent)',
                    }}
                  >
                    {sessionLabel(s.dateIso, s.date, s.index)}
                    <span className="ml-1.5 font-bold opacity-75">{count}</span>
                  </button>
                );
              })}
            </div>
          )}

          <input
            ref={inputRef}
            type="search"
            placeholder={plan ? 'Find a swimmer in my plan…' : 'Find a swimmer by name — when do they swim?'}
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            className="min-w-[180px] flex-1 rounded-[10px] border px-3 py-2 text-[13px]"
            style={{
              borderColor: plan ? 'var(--theme-personal-border)' : 'var(--deep-card-border)',
              background: plan ? 'var(--theme-personal-badge-bg)' : 'var(--deep-card-bg-raised)',
              color: 'var(--deep-text)',
            }}
            dir="auto"
          />
        </div>

        {/* ── Строка 3: люди. В прилипшем виде её нет — имена ушли в строку 2 текстом. ── */}
        {!stuck && showPeople && (
          <div className="-mx-3 mt-2 flex items-center gap-1.5 overflow-x-auto px-3 sm:mx-0 sm:flex-wrap sm:px-0">
            <span
              className="flex-none text-[10px] font-black uppercase tracking-wide"
              style={{ color: plan ? 'var(--theme-personal-accent)' : 'var(--deep-text-mute)' }}
            >
              {plan ? '⭐ Plan' : '❤️ Favorites'}
            </span>

            {swimmerChips.map((p) => (
              <PersonChip
                key={`s${p.id}`}
                name={p.name}
                gold={plan || !!p.favorite}
                removable={plan}
                onClick={() => (plan ? onRemoveSwimmer(p.id) : pick(p.id))}
              />
            ))}

            {/* Клуб-чип одинаков в обеих вкладках (правило хендоффа). В All у клуба нет
                ответа «во сколько плывёт», поэтому тап переводит в My plan — там он есть. */}
            {clubChips.map((c) => (
              <ClubChip
                key={`c${c.id}`}
                club={c}
                removable={plan}
                onClick={() => (plan ? onRemoveClub(c.id) : onModeChange('plan'))}
              />
            ))}

            {plan && (
              <>
                <button
                  type="button"
                  onClick={onEditPlan}
                  className="flex-none whitespace-nowrap rounded-full px-3 py-[6px] text-[12.5px] font-extrabold"
                  style={{
                    border: '1px dashed var(--theme-personal-border)',
                    color: 'var(--theme-personal-accent)',
                    background: 'transparent',
                  }}
                >
                  ＋ Add swimmer / club
                </button>
                <button
                  type="button"
                  onClick={onEditPlan}
                  className="ml-auto flex-none whitespace-nowrap rounded-full px-3 py-[6px] text-[12.5px] font-black"
                  style={{ background: 'var(--deep-accent-hover)', color: 'var(--deep-accent-ink)' }}
                >
                  Edit ▾
                </button>
              </>
            )}
          </div>
        )}
      </div>

      {/* Выдача поиска — ПОД панелью, а не внутри: она разворачивается на пол-экрана и
          внутри рамки ломала бы высоту зоны. */}
      {query.trim().length >= 2 && (
        <div
          className="mt-2 max-h-[50vh] overflow-y-auto rounded-[10px] border"
          style={{ borderColor: 'var(--deep-card-border)', background: 'var(--deep-card-bg)' }}
        >
          {loading && !hits && <div className="p-3 text-sm opacity-60">Searching…</div>}
          {hits?.length === 0 && (
            <div className="p-3 text-sm opacity-60">
              {plan ? 'Nobody with that name is in your plan.' : 'Nobody with that name is entered here.'}
            </div>
          )}
          {hits?.map((h) => (
            <button
              key={h.swimmer_id}
              type="button"
              onClick={() => pick(h.swimmer_id)}
              className="flex w-full items-center gap-3 border-b px-3 py-2 text-left last:border-b-0"
              style={{ borderColor: 'var(--deep-divider)' }}
            >
              <div className="min-w-0 flex-1">
                <div className="truncate text-sm font-bold" dir="auto">{h.swimmer_name}</div>
                <div className="truncate text-[11px] opacity-70" dir="auto">
                  {[h.birth_year, h.club_name].filter(Boolean).join(' · ')}
                </div>
              </div>
              <div className="shrink-0 text-right text-[11px] opacity-80">
                {/* Дни — то, ради чего поиск и заведён: у составного старта пловец плывёт
                    в свой день, и лента программы этого не подсказывает. */}
                <div className="font-bold">{h.days.map(dayLabel).join(' · ')}</div>
                <div className="opacity-70">
                  {h.swims} {h.swims === 1 ? 'swim' : 'swims'}
                  {h.first_start_at ? ` · ${formatApproxTime(h.first_start_at)}` : ''}
                </div>
              </div>
            </button>
          ))}
        </div>
      )}
    </div>

    {/* Кнопка «наверх» — пока шапка погашена, это единственный короткий путь обратно к
        табам соревнования и селектору. Портал в body: зона липкая, и кнопка внутри неё
        ездила бы вместе с ней вместо того, чтобы стоять у нижнего края экрана.
        Место — левее переключателя темы (`UI_ModeToggle`, bottom-4 right-4, 40px). */}
    {stuck && createPortal(
      <button
        type="button"
        onClick={() => window.scrollTo({ top: 0, behavior: 'smooth' })}
        aria-label="Back to top"
        title="Back to top"
        className="fixed bottom-4 right-[68px] z-[110] flex h-11 w-11 items-center justify-center rounded-full shadow-lg"
        // Токены СТРАНИЧНЫЕ, а не `--deep-*`: кнопка живёт в портале на body, снаружи
        // контейнера с классом темы таба, где `--deep-*` объявлены, — там они пустые, и
        // кнопка выходила прозрачной. Пара «фон primary / текст accent-text» — правило
        // парных токенов.
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

/** Половина сегмент-контрола: активная — плашка цвета карточки с тенью, неактивная — прозрачная. */
function ModeSegment({ active, onClick, label, color }: {
  active: boolean; onClick: () => void; label: string; color: string;
}) {
  return (
    <button
      type="button"
      role="tab"
      aria-selected={active}
      onClick={onClick}
      className="flex-1 whitespace-nowrap rounded-[10px] px-3 py-[7px] text-[12.5px] font-extrabold sm:flex-none"
      style={{
        background: active ? 'var(--deep-card-bg)' : 'transparent',
        boxShadow: active ? 'var(--deep-card-shadow)' : 'none',
        color: active ? color : 'var(--deep-text-mute)',
      }}
    >
      {label}
    </button>
  );
}

function PersonChip({ name, gold, removable, onClick }: {
  name: string; gold: boolean; removable: boolean; onClick: () => void;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      title={removable ? 'Remove from my plan' : undefined}
      className="flex flex-none items-center gap-1.5 whitespace-nowrap rounded-full border px-3 py-[6px] text-[12.5px] font-extrabold"
      style={{
        borderColor: gold ? 'var(--theme-personal-border)' : 'var(--deep-card-border)',
        background: gold ? 'var(--theme-personal-badge-bg)' : 'transparent',
        color: gold ? 'var(--theme-personal-accent)' : 'var(--deep-text-mute)',
      }}
    >
      {removable && <span className="opacity-60" aria-hidden>✕</span>}
      <span dir="auto">{name}</span>
    </button>
  );
}

/**
 * Клуб: эмблема из манифеста (`UI_ClubIcon`, свой фоллбек «no-club»), имя и суффикс «club» —
 * иначе чип клуба не отличить от чипа пловца. В макете на его месте кружок с инициалами:
 * у макета эмблем не было, у нас они есть.
 */
function ClubChip({ club, removable, onClick }: {
  club: FilterZoneClub; removable: boolean; onClick: () => void;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      title={removable ? 'Remove from my plan' : undefined}
      className="flex flex-none items-center gap-1.5 whitespace-nowrap rounded-full border py-[5px] pl-1.5 pr-3 text-[12.5px] font-extrabold"
      style={{
        borderColor: 'var(--deep-accent-border)',
        background: 'var(--deep-accent-chip)',
        color: 'var(--deep-accent)',
      }}
    >
      {removable && <span className="ml-1 opacity-60" aria-hidden>✕</span>}
      <span className="flex h-5 w-5 flex-none items-center justify-center overflow-hidden rounded-full">
        <UI_ClubIcon clubName={club.name} iconWidth="20px" />
      </span>
      <span dir="auto">{club.name}</span>
      <span className="text-[10.5px] font-bold" style={{ color: 'var(--deep-text-mute)' }}>club</span>
    </button>
  );
}
