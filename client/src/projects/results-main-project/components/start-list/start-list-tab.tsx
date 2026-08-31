import React, { useCallback, useEffect, useRef, useState } from 'react';
import '../../../components/deep/deep-theme.css';
import FollowingPicker from './following-picker';
import PlanCard from './plan-card';
import HeatZoom from './heat-zoom';
import ProgrammeZoom from './programme-zoom';
import SourceTabs from './source-tabs';
import SwimmerFinder from './swimmer-finder';
import SwimmerZoom from './swimmer-zoom';
import { useStartListPlan } from './use-start-list-plan';
import { useEffectivePlan } from './use-effective-plan';
import { useMode } from '../../../../hooks/useMode';
import { useAuth } from '../../../../hooks/useAuth';
import { parsePlanParam, planSummary, serializePlanParam } from './plan-model';
import type { StartListSource } from '../competition-header/types';

/**
 * Таб «Start list» карточки соревнования — МАРШРУТИЗАТОР экранов и адрес.
 *
 * Сами экраны живут отдельными модулями рядом (шаг Т4 разбора,
 * docs/plans/start-list-ticket-plan.md): здесь только «какой экран показать», «что стоит
 * в адресе» и «какой источник активен». Разбор понадобился до Т5–Т7: к трём существующим
 * зумам добавляются пикер Following, карточка плана и экран «протокол не опубликован»,
 * а в одном файле это уже не читалось.
 *
 * Адрес: `?swimmer=` открывает карточку пловца, `?heat=` — заплыв, ничего — программу
 * (решение 2 плана). `?src=` — активный источник у составного старта.
 *
 * Мобильный — основной вид (решение 6): никакой горизонтальной прокрутки. Автообновления
 * нет (решение 7) — вместо него «Updated HH:MM» + Refresh на каждом экране.
 */

type Zoom = 'programme' | 'heat' | 'swimmer' | 'picker' | 'plan';

interface Props {
  orgCompId: number;
  /** Источники протокола (подтабы). Пусто или один — подтабы не рисуются, вид как был. */
  sources?: StartListSource[];
}

export default function StartListTab({ orgCompId, sources = [] }: Props) {
  // Активный источник переживает перезагрузку и пересылку ссылки — держим в ?src=.
  // Неизвестный/чужой src игнорируем: иначе подтаб «выбран», а данных под ним нет.
  const [activeSource, setActiveSource] = useState<number>(() => {
    const raw = Number(new URLSearchParams(window.location.search).get('src'));
    return sources.some((s) => s.org_comp_id === raw) ? raw : orgCompId;
  });
  const effectiveOrgCompId = sources.some((s) => s.org_comp_id === activeSource) ? activeSource : orgCompId;

  const readQuery = () => {
    const q = new URLSearchParams(window.location.search);
    const swimmer = Number(q.get('swimmer'));
    const heat = Number(q.get('heat'));
    if (Number.isFinite(swimmer) && swimmer > 0) return { zoom: 'swimmer' as Zoom, swimmerId: swimmer, orgDisciplineId: null as number | null, heat: null as number | null };
    if (Number.isFinite(heat) && heat > 0) return { zoom: 'heat' as Zoom, swimmerId: null as number | null, orgDisciplineId: heat, heat: null as number | null };
    return { zoom: 'programme' as Zoom, swimmerId: null as number | null, orgDisciplineId: null as number | null, heat: null as number | null };
  };

  const [state, setState] = useState(readQuery);

  const setUrl = useCallback((params: { swimmer?: number | null; heat?: number | null }) => {
    const url = new URL(window.location.href);
    url.searchParams.delete('swimmer');
    url.searchParams.delete('heat');
    if (params.swimmer != null) url.searchParams.set('swimmer', String(params.swimmer));
    if (params.heat != null) url.searchParams.set('heat', String(params.heat));
    window.history.replaceState(null, '', url.toString());
  }, []);

  const openProgramme = useCallback(() => { setUrl({}); setState({ zoom: 'programme', swimmerId: null, orgDisciplineId: null, heat: null }); }, [setUrl]);
  const openEvent = useCallback((orgDisciplineId: number) => { setUrl({ heat: orgDisciplineId }); setState({ zoom: 'heat', swimmerId: null, orgDisciplineId, heat: null }); }, [setUrl]);
  const openSwimmer = useCallback((swimmerId: number) => { setUrl({ swimmer: swimmerId }); setState({ zoom: 'swimmer', swimmerId, orgDisciplineId: null, heat: null }); }, [setUrl]);

  // Разворот заплыва из карточки пловца — открываем зум 2 той дисциплины. Заодно
  // переключаем ПОДТАБ: карточка собрана из всех источников, и заплыв может лежать не в
  // том, что открыт сейчас; без переключения зум 2 запросил бы дисциплину у чужого compID
  // и показал «нет данных».
  const openHeatFromSwimmer = useCallback((srcOrgCompId: number, orgDisciplineId: number, heat: number) => {
    const url = new URL(window.location.href);
    url.searchParams.set('src', String(srcOrgCompId));
    url.searchParams.set('heat', String(orgDisciplineId));
    url.searchParams.delete('swimmer');
    window.history.replaceState(null, '', url.toString());
    setActiveSource(srcOrgCompId);
    setState({ zoom: 'heat', swimmerId: null, orgDisciplineId, heat });
  }, []);

  // Смена источника всегда возвращает на зум 1: heat/swimmer принадлежат ПРОШЛОМУ
  // протоколу, и в новом их идентификаторов может не быть вовсе.
  const selectSource = useCallback((next: number) => {
    const url = new URL(window.location.href);
    url.searchParams.set('src', String(next));
    url.searchParams.delete('swimmer');
    url.searchParams.delete('heat');
    window.history.replaceState(null, '', url.toString());
    setActiveSource(next);
    setState({ zoom: 'programme', swimmerId: null, orgDisciplineId: null, heat: null });
  }, []);

  // Все источники соревнования: поиск и карточка пловца работают по ним ЦЕЛИКОМ, а не по
  // активному подтабу — родителю всё равно, в каком окружном протоколе плывёт его ребёнок.
  const allOrgCompIds = sources.length > 0 ? sources.map((s) => s.org_comp_id) : [orgCompId];

  // Личный план (Т3) — на СОРЕВНОВАНИЕ целиком, а не на активный источник: у составного
  // старта подтабы это разные протоколы одного и того же чемпионата.
  const { plan: savedPlan, save, loading: planLoading } = useStartListPlan(orgCompId);

  // Состав, приехавший ССЫЛКОЙ (?plan=s10,c506). Показываем его, но в свой план НЕ пишем:
  // человек мог открыть чужую ссылку из чата, и подменять ему собственный состав нельзя —
  // это происходит только когда он сам нажмёт Edit (правило Т10).
  //
  // Считается ДО useEffectivePlan: пловцов из ссылки надо загрузить, иначе у получателя
  // (у которого их нет ни в избранном, ни в своём плане) карточка пустая — см.
  // `alsoLoadSwimmerIds`.
  const [sharedPlan, setSharedPlan] = useState(
    () => parsePlanParam(new URLSearchParams(window.location.search).get('plan')),
  );

  // Действующий состав и данные под него — один расчёт на пикер, карточку и на решение
  // «какой экран открыть первым» (useEffectivePlan).
  const effective = useEffectivePlan(allOrgCompIds, savedPlan, sharedPlan?.swimmer_ids ?? []);
  const planLabel = planSummary(effective.plan);

  const openPicker = useCallback(() => setState((s) => ({ ...s, zoom: 'picker' })), []);
  const openPlan = useCallback(() => setState((s) => ({ ...s, zoom: 'plan' })), []);
  const backToProgramme = useCallback(() => setState((s) => ({ ...s, zoom: 'programme' })), []);

  // Токены темы Deep (`--deep-*`) объявлены НА КЛАССЕ, а не на :root, поэтому их надо
  // включить на контейнере — иначе весь принятый дизайн таба (карточки, cyan-акценты)
  // рисуется пустыми переменными. Пара light↔dark идёт за глобальным режимом страницы.
  const { mode } = useMode();
  const deepThemeClass = mode === 'dark' ? 'theme-deep' : 'theme-deep-light';

  const shownPlan = sharedPlan ?? effective.plan;

  // Ссылка «поделиться» — этот же адрес плюс состав. Строится от ТЕКУЩЕГО адреса, чтобы
  // ссылка вела ровно на то соревнование, которое человек и смотрит.
  const shareUrl = (() => {
    const url = new URL(window.location.href);
    url.searchParams.set('tab', 'startlist');
    url.searchParams.set('plan', serializePlanParam(shownPlan));
    url.searchParams.delete('swimmer');
    url.searchParams.delete('heat');
    return url.toString();
  })();

  // Любая правка состава делает план СВОИМ: сохраняем и убираем ?plan= из адреса, иначе
  // после перезагрузки вернулся бы чужой состав.
  const saveOwn = useCallback((next: typeof effective.plan) => {
    setSharedPlan(null);
    const url = new URL(window.location.href);
    url.searchParams.delete('plan');
    window.history.replaceState(null, '', url.toString());
    save(next);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [save]);

  // Экраны личного плана — пикер и карточка. Пустой состав карточке показывать нечего,
  // поэтому вход ведёт в пикер, пока в плане никого нет.
  const inPlan = state.zoom === 'picker' || state.zoom === 'plan';
  const hasPlan = !effective.isEmpty || sharedPlan != null;

  // Экран «протокол не опубликован» (Т9): подписка «Notify me» живёт в плане и только у
  // залогиненного — гостю её негде хранить. Рассылки за флагом пока нет, он копится.
  const { isAuthenticated } = useAuth();
  const notify = {
    isAuthenticated,
    notifyMe: savedPlan?.notify_me ?? false,
    onToggle: isAuthenticated
      ? (next: boolean) => save({ ...effective.plan, notify_me: next })
      : null,
  };
  // Дата старта для того же экрана — из подтабов источников, если они есть.
  const startsLabel = sources.length > 0 ? (sources[0].date ?? null) : null;

  // Опубликован ли протокол (сообщает зум 1). null — ещё не знаем: до ответа ничего не
  // прячем, иначе кнопка плана моргает на каждой загрузке.
  const [published, setPublished] = useState<boolean | null>(null);

  // ГЛАВНЫЙ ЭКРАН ТАБА — карточка плана, а не программа: родитель пришёл за «когда плывёт
  // мой», и заставлять его сперва накликать состав неправильно. Как только состав известен
  // (свой план или дефолт из избранного), открываем её сами — но ровно один раз и только
  // если человек ещё никуда не ушёл сам (адрес не указывает на заплыв/пловца).
  const autoOpened = useRef(false);
  useEffect(() => {
    if (autoOpened.current || planLoading || effective.loading) return;
    autoOpened.current = true;
    if ((!effective.isEmpty || sharedPlan != null) && state.zoom === 'programme') {
      setState((s) => ({ ...s, zoom: 'plan' }));
    }
  }, [planLoading, effective.loading, effective.isEmpty, state.zoom]);

  return (
    // Цвет текста задаётся ЯВНО: на контейнере страницы результатов висит легаси-класс
    // `.dolphine-training`, а в `results-table.css` он красит текст в green — и всё, что
    // своего цвета не ставит, наследовало его. Карточка плана красит себя сама, поэтому
    // зелёной была только программа. Токен парный фону таба (`--deep-page-bg`).
    <div className={deepThemeClass} style={{ color: 'var(--deep-text)' }}>
      {/* Два таба вместо одной кнопки-переключателя (решение Влада 30.08.2026). Кнопка
          меняла надпись по состоянию («My plan» ↔ «← Back to the programme»), и по ней не
          было видно, что экрана ДВА и какой сейчас открыт. Табы показывают оба сразу.

          Не `DeepTabs`: тот собран как «папка», сросшаяся с панелью контента
          (`border-bottom: none`, `margin-bottom: -1px`, парная `.deep-tabs-panel`), а
          карточка-билет внутрь панели не встаёт — её вырезы-полукруги закрашены фоном
          СТРАНИЦЫ, и на панели они превратились бы в чужие кружки. Отступление записано
          в docs/ui-components.md §6. */}
      {published !== false && (
        <div className="mb-2 mt-2 grid grid-cols-2 gap-2" role="tablist" aria-label="Start list view">
          <StartListViewTab
            active={!inPlan}
            onClick={backToProgramme}
            label="All programme"
            sub="everyone at this meet"
          />
          {/* Таб плана всегда в цвете избранного — это «мои», а не просто второй экран. */}
          <StartListViewTab
            active={inPlan}
            onClick={hasPlan ? openPlan : openPicker}
            label="⭐ My plan"
            sub={planLabel ?? 'choose who to follow'}
            personal
          />
        </div>
      )}

      {/* Панель «найти своего» — над зумами: это вход в таб для того, кто пришёл по одному
          вопросу «когда плывёт мой». На экранах плана она лишняя — там уже свой состав. */}
      {!inPlan && published !== false && (
        <SwimmerFinder orgCompIds={allOrgCompIds} onOpenSwimmer={openSwimmer} />
      )}

      {state.zoom === 'picker' && (
        <FollowingPicker
          orgCompIds={allOrgCompIds}
          plan={shownPlan}
          swimmers={effective.swimmers}
          clubs={effective.clubs}
          rowIds={effective.rowIds}
          favClubIds={effective.favClubIds}
          primarySwimmerId={effective.primarySwimmerId}
          loading={effective.loading}
          onChange={saveOwn}
          onShowPlan={hasPlan ? openPlan : backToProgramme}
        />
      )}

      {state.zoom === 'plan' && (
        <PlanCard
          orgCompIds={allOrgCompIds}
          plan={shownPlan}
          swimmers={effective.swimmers}
          clubs={effective.clubs}
          planLoading={effective.loading}
          shareUrl={shareUrl}
          shared={sharedPlan != null}
          onChange={saveOwn}
          onEdit={openPicker}
          onOpenHeat={openHeatFromSwimmer}
        />
      )}
      {/* Подтабы источников относятся к зумам, а не к пикеру: там выбирают, ЗА КЕМ следить,
          и «какой протокол смотрим» на этом экране ничего не значит. */}
      {sources.length > 1 && !inPlan && (
        <SourceTabs
          sources={sources}
          activeOrgCompId={effectiveOrgCompId}
          onSelect={selectSource}
        />
      )}
      {state.zoom === 'programme' && (
        <ProgrammeZoom
          orgCompId={effectiveOrgCompId}
          startsLabel={startsLabel}
          notify={notify}
          onPublished={setPublished}
          onOpenEvent={openEvent}
        />
      )}
      {state.zoom === 'heat' && state.orgDisciplineId != null && (
        <HeatZoom orgCompId={effectiveOrgCompId} orgDisciplineId={state.orgDisciplineId} heat={state.heat} onBack={openProgramme} onOpenSwimmer={openSwimmer} />
      )}
      {state.zoom === 'swimmer' && state.swimmerId != null && (
        <SwimmerZoom
          orgCompIds={allOrgCompIds}
          swimmerId={state.swimmerId}
          onBack={openProgramme}
          onOpenHeat={openHeatFromSwimmer}
        />
      )}
    </div>
  );
}

/**
 * Плитка-таб «какой экран таба смотрим»: программа целиком или личный план.
 *
 * Активная — залитая своим цветом, неактивная — прозрачная и приглушённая, но того же
 * оттенка: у плана он золотой (цвет избранного), у программы — обычный акцент таба.
 * Подпись второй строкой отвечает на «а что там»: у плана это состав («1 swimmer + 1
 * club»), у программы — что она про всех.
 */
function StartListViewTab({ active, onClick, label, sub, personal = false }: {
  active: boolean;
  onClick: () => void;
  label: string;
  sub: string;
  /** true — таб личного плана: цвет избранного вместо акцента таба. */
  personal?: boolean;
}) {
  const accent = personal ? 'var(--theme-personal-accent)' : 'var(--deep-accent)';
  return (
    <button
      type="button"
      role="tab"
      aria-selected={active}
      onClick={onClick}
      className="rounded-[12px] border px-3 py-2 text-left"
      style={{
        background: active
          ? (personal ? 'var(--theme-personal-bg)' : 'var(--deep-accent-soft)')
          : 'transparent',
        borderColor: active
          ? (personal ? 'var(--theme-personal-border)' : 'var(--deep-accent-border)')
          : 'var(--deep-card-border)',
        color: accent,
        // Неактивный таб глушим целиком, а не подменой цвета: оттенок должен остаться
        // узнаваемым (золото = «мои»), иначе он перестаёт читаться как тот же экран.
        opacity: active ? 1 : 0.55,
      }}
    >
      <span className="block truncate text-[13px] font-black">{label}</span>
      <span className="block truncate text-[11px] font-bold" style={{ color: 'var(--deep-text-mute)' }}>
        {sub}
      </span>
    </button>
  );
}
