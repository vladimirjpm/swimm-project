import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import '../../../components/deep/deep-theme.css';
import FilterZone from './filter-zone';
import FollowingPicker from './following-picker';
import PlanCard from './plan-card';
import HeatZoom from './heat-zoom';
import ProgrammeZoom from './programme-zoom';
import SwimmerZoom from './swimmer-zoom';
import { useStartListClubSwims } from './use-start-list';
import { useStartListPlan } from './use-start-list-plan';
import { useStartListSessions } from './use-start-list-sessions';
import { useEffectivePlan } from './use-effective-plan';
import { useDeepThemeClass } from '../../../components/deep/use-deep-theme-class';
import { useAuth } from '../../../../hooks/useAuth';
import { useFavoritesContext } from '../../../../hooks/favorites-context';
import { sortByFavoriteRank } from '../../../../utils/helpers/favorites-order';
import {
  assemblePlanSwims, parsePlanParam, planRowsBySession, serializePlanParam,
} from './plan-model';
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

// 'picker' здесь больше нет: правка состава стала МОДАЛКОЙ поверх текущего экрана
// (решение Влада 31.08.2026), и отдельной ветки маршрутизатора ей не нужно.
type Zoom = 'programme' | 'heat' | 'swimmer' | 'plan';

interface Props {
  orgCompId: number;
  /** Источники протокола (подтабы). Пусто или один — подтабы не рисуются, вид как был. */
  sources?: StartListSource[];
}

export default function StartListTab({ orgCompId, sources = [] }: Props) {
  const readQuery = () => {
    const q = new URLSearchParams(window.location.search);
    const swimmer = Number(q.get('swimmer'));
    const heat = Number(q.get('heat'));
    if (Number.isFinite(swimmer) && swimmer > 0) return { zoom: 'swimmer' as Zoom, swimmerId: swimmer, orgDisciplineId: null as number | null, heat: null as number | null };
    if (Number.isFinite(heat) && heat > 0) return { zoom: 'heat' as Zoom, swimmerId: null as number | null, orgDisciplineId: heat, heat: null as number | null };
    // Ссылка «Share» (`?plan=`) ведёт в карточку плана СРАЗУ, без ожидания загрузок:
    // состав в ней уже известен, и ждать автооткрытия незачем — оно зависит от порядка
    // ответов и на холодной загрузке промахивалось.
    const zoom: Zoom = parsePlanParam(q.get('plan')) != null ? 'plan' : 'programme';
    return { zoom, swimmerId: null as number | null, orgDisciplineId: null as number | null, heat: null as number | null };
  };

  const [state, setState] = useState(readQuery);
  // Автооткрытие карточки плана уже не нужно (см. эффект ниже): таб открыт не на программе.
  const autoOpened = useRef(state.zoom !== 'programme');

  const setUrl = useCallback((params: { swimmer?: number | null; heat?: number | null }) => {
    const url = new URL(window.location.href);
    url.searchParams.delete('swimmer');
    url.searchParams.delete('heat');
    if (params.swimmer != null) url.searchParams.set('swimmer', String(params.swimmer));
    if (params.heat != null) url.searchParams.set('heat', String(params.heat));
    window.history.replaceState(null, '', url.toString());
  }, []);

  const openProgramme = useCallback(() => {
    autoOpened.current = true;   // вернулся на программу сам — план поверх не открываем
    setUrl({});
    setState({ zoom: 'programme', swimmerId: null, orgDisciplineId: null, heat: null });
  }, [setUrl]);
  const openEvent = useCallback((orgDisciplineId: number) => { setUrl({ heat: orgDisciplineId }); setState({ zoom: 'heat', swimmerId: null, orgDisciplineId, heat: null }); }, [setUrl]);
  const openSwimmer = useCallback((swimmerId: number) => { setUrl({ swimmer: swimmerId }); setState({ zoom: 'swimmer', swimmerId, orgDisciplineId: null, heat: null }); }, [setUrl]);

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

  // Пикер — модалка поверх экрана; под ней остаётся карточка плана, к которой человек и
  // вернётся. Пустой состав тоже открывает карточку: она честно скажет, что никого нет.
  const [pickerOpen, setPickerOpen] = useState(false);
  const openPlan = useCallback(() => setState((s) => ({ ...s, zoom: 'plan' })), []);
  const openPicker = useCallback(() => {
    setState((s) => ({ ...s, zoom: 'plan' }));
    setPickerOpen(true);
  }, []);
  const backToProgramme = useCallback(() => {
    autoOpened.current = true;   // «All programme» нажат руками — план поверх не открываем
    setState((s) => ({ ...s, zoom: 'programme' }));
  }, []);

  // Токены темы Deep (`--deep-*`) объявлены НА КЛАССЕ, а не на :root, поэтому их надо
  // включить на контейнере — иначе весь принятый дизайн таба (карточки, cyan-акценты)
  // рисуется пустыми переменными. Пара light↔dark идёт за глобальным режимом страницы.
  const deepThemeClass = useDeepThemeClass();

  const shownPlan = sharedPlan ?? effective.plan;

  // Ссылка «поделиться» — этот же адрес плюс состав. Строится от ТЕКУЩЕГО адреса, чтобы
  // ссылка вела ровно на то соревнование, которое человек и смотрит.
  //
  // ОТКРЫТЫЙ ПЛОВЕЦ ВХОДИТ В СОСТАВ ссылки (правка 31.08.2026): нашёл ребёнка поиском,
  // открыл его карточку, нажал «Share» — получатель должен увидеть план именно с ним, а не
  // пустой. Пловец добавляется К составу, а не вместо него: свой ребёнок из плана из ссылки
  // не пропадает.
  //
  // `?swimmer=`/`?heat=` из ссылки вычищаются: получателю открывается КАРТОЧКА ПЛАНА, а не
  // тот зум, в котором стоял отправитель. Пустой состав в адрес не пишем вовсе — иначе
  // ссылка несла бы `plan=` ни с чем и получатель видел бы пустой билет.
  const sharePlan = state.zoom === 'swimmer' && state.swimmerId != null
    ? { ...shownPlan, swimmer_ids: [...new Set([...shownPlan.swimmer_ids, state.swimmerId])] }
    : shownPlan;

  const shareUrl = (() => {
    const url = new URL(window.location.href);
    url.searchParams.set('tab', 'startlist');
    const param = serializePlanParam(sharePlan);
    if (param) url.searchParams.set('plan', param);
    else url.searchParams.delete('plan');
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
  const inPlan = state.zoom === 'plan';
  const hasPlan = !effective.isEmpty || sharedPlan != null;

  // ── Данные зоны фильтров (5d) ────────────────────────────────────────────────
  // Заплывы плана собираются ЗДЕСЬ, а не в карточке: их число нужно чипам сессий и
  // сегменту «My plan · N» ещё до того, как карточка открыта. Карточка получает готовый
  // список пропом — второй такой же расчёт внутри неё разъехался бы с чипами.
  const { data: clubSwims, loading: clubSwimsLoading } = useStartListClubSwims(allOrgCompIds, shownPlan.club_ids);
  const planSwims = useMemo(
    () => assemblePlanSwims(effective.swimmers, clubSwims, shownPlan.swimmer_ids),
    [effective.swimmers, clubSwims, shownPlan.swimmer_ids.join(',')],
  );
  const planRows = useMemo(() => planRowsBySession(planSwims), [planSwims]);
  const planSwimsTotal = useMemo(
    () => [...planRows.values()].reduce((sum, n) => sum + n, 0),
    [planRows],
  );
  // Поиск в режиме плана сужается до СВОИХ — включая тех, кто попал в план через клуб:
  // берём id из собранных заплывов, а не из plan.swimmer_ids.
  // Точки «здесь плывут свои» в программе (решение Влада 22.10.2026): золото — заплыв
  // выбранного пловца, синий — заплыв кого-то из выбранного клуба. Клубные считаем по ВСЕМ
  // заплывам клуба, а не по `planSwims`: там выбранный лично перебивает клубного, и у
  // заплыва, где плывёт только он, синей точки не стало бы.
  const followed = useMemo(() => ({
    swimmerEvents: new Set(planSwims.filter((s) => s.mine).map((s) => s.swim.org_discipline_id)),
    clubEvents: new Set(clubSwims.map((s) => s.org_discipline_id)),
  }), [planSwims, clubSwims]);
  const planSwimmerIds = useMemo(
    () => new Set(planSwims.map((s) => s.swim.swimmer_id)),
    [planSwims],
  );

  // Сессии (протокол × день) и открытая из них — `useStartListSessions`, один выбор на
  // оба режима, адрес `?src=&day=`.
  const {
    sessions, activeSession, activeOrgCompId: effectiveOrgCompId, activeDay: activeDayIso,
    openSession, openSource,
  } = useStartListSessions(orgCompId, sources, planRows);

  // Разворот заплыва из карточки пловца — открываем зум 2 той дисциплины. Заодно
  // переключаем ПОДТАБ: карточка собрана из всех источников, и заплыв может лежать не в
  // том, что открыт сейчас; без переключения зум 2 запросил бы дисциплину у чужого compID
  // и показал «нет данных».
  const openHeatFromSwimmer = useCallback((srcOrgCompId: number, orgDisciplineId: number, heat: number) => {
    openSource(srcOrgCompId);
    setUrl({ heat: orgDisciplineId });
    setState({ zoom: 'heat', swimmerId: null, orgDisciplineId, heat });
  }, [openSource, setUrl]);

  const { favorites, primarySwimmerId: favPrimaryId, familySwimmerIds } = useFavoritesContext();
  // Строка 3 в All — избранные: пловцы, заявленные на этом старте, и избранные клубы.
  // Показывать того, кого в протоколе нет, значит обещать заплывы, которых не будет.
  // Порядок — Me → семья → остальные, общим хелпером (family-favorites-plan.md).
  const favSwimmers = useMemo(() => sortByFavoriteRank(
    favorites.filter((f) => f.swimmer_id != null && effective.swimmers[f.swimmer_id as number]),
    (f) => f.swimmer_id, favPrimaryId, familySwimmerIds,
  )
    .map((f) => ({
      id: f.swimmer_id as number,
      // Имя чипа — из карточки протокола, а не из избранного: в избранном оно могло быть
      // сохранено на другом языке, а на витрине имена ивритские (правило проекта).
      name: effective.swimmers[f.swimmer_id as number]?.swimmer_name ?? f.swimmer_name ?? `#${f.swimmer_id}`,
      favorite: true,
    })),
    [favorites, effective.swimmers, favPrimaryId, familySwimmerIds]);
  const favClubs = useMemo(() => effective.clubs
    .filter((c) => effective.favClubIds.includes(c.club_id))
    .map((c) => ({ id: c.club_id, name: c.club_name, swimmers: c.swimmers })),
    [effective.clubs, effective.favClubIds]);

  const planSwimmerChips = sortByFavoriteRank(shownPlan.swimmer_ids, (id) => id, favPrimaryId, familySwimmerIds).map((id) => ({
    id,
    name: effective.swimmers[id]?.swimmer_name ?? `#${id}`,
  }));
  const planClubChips = shownPlan.club_ids.map((id) => {
    const club = effective.clubs.find((c) => c.club_id === id);
    return { id, name: club?.club_name ?? `#${id}`, swimmers: club?.swimmers ?? 0 };
  });

  // Вход в план ждёт выбора дня: если открытый день пуст для меня, план уйдёт на первый
  // день с моими заплывами (эффект ниже). Ждёт ровно до первого решения — дальше дни
  // выбирает человек, и пустой день он открывает сознательно (замечание Влада 22.10.2026:
  // «на My plan даты не реагируют» — чип пустого дня был выключен, а потом ещё и
  // переключался обратно этим же переходом).
  const planDayPending = useRef(state.zoom === 'plan');

  // Переключение режима зоны. «My plan» с пустым составом ведёт в пикер: карточке нечего
  // показывать, пока не выбран никто.
  const setMode = useCallback((next: 'all' | 'plan') => {
    if (next === 'all') { backToProgramme(); return; }
    planDayPending.current = true;
    if (hasPlan) openPlan();
    // Состав пуст — сразу поверх карточки открываем пикер: показывать пустой билет и
    // ждать, что человек сам найдёт «Add swimmer», неправильно.
    else openPicker();
  }, [backToProgramme, hasPlan, openPlan, openPicker]);

  // Выбор сессии — ОДИН на оба режима (день, выбранный в All, остаётся и в My plan). В All
  // смена сессии ещё и возвращает на программу: heat/swimmer принадлежат ПРОШЛОМУ
  // протоколу. В плане меняется только фильтр.
  const selectSession = useCallback((key: string) => {
    const next = sessions.find((s) => s.key === key);
    if (!next) return;
    planDayPending.current = false;
    openSession(next);
    if (!inPlan) {
      setUrl({});
      setState({ zoom: 'programme', swimmerId: null, orgDisciplineId: null, heat: null });
    }
  }, [sessions, inPlan, openSession, setUrl]);

  // Переход «на день с моими» при входе в план. Срабатывает, только когда счётчики уже
  // посчитаны (иначе у всех дней 0 и решать нечем) — и один раз.
  useEffect(() => {
    if (!inPlan || !planDayPending.current || planSwimsTotal === 0 || !activeSession) return;
    planDayPending.current = false;
    if (activeSession.mine > 0) return;
    const withMine = sessions.find((s) => s.mine > 0);
    if (withMine) openSession(withMine);
  }, [inPlan, planSwimsTotal, activeSession, sessions, openSession]);

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
  //
  // «Ещё никуда не ушёл» держит флаг `autoOpened` (объявлен у состояния зума): он взведён
  // сразу, если таб открылся НЕ на программе (`?plan=`, `?swimmer=`, `?heat=`), и при любом
  // ручном возврате на программу. Иначе ожидание загрузки доживало до клика «All programme»
  // и через секунду возвращало человека в план (поймано на проде 21.10.2026, 16854).
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
    // Узкая колонка: полезных пикселей в строке заплыва ~230, и на широком мониторе лента
    // растягивалась вчетверо шире, чем ей нужно. Лесенка `content-box-*` — в index.css,
    // сменить ширину = сменить одно слово в классе.
    <div className={`${deepThemeClass} content-box-xs`} style={{ color: 'var(--deep-text)' }}>
      {/* ЗОНА ФИЛЬТРОВ (вариант 5d/5dm, !design_handoff/FILTER-ZONE-5D) — одна панель на
          оба режима таба: сегмент All programme / My plan, чипы сессий, поиск, люди.
          До неё это были пять разных блоков (плитки-переключатель, строка Share, панель
          «найти своего», подтабы источников и дни-чипы внутри карточки плана) высотой
          ~400px.

          Липкой панель НЕ сделана, хотя переключатель до 5d липнул: в 5dm зона ~230px, и
          вместе с компакт-баром шапки она съедала бы почти половину экрана телефона.
          Открытый вопрос — см. итог работы. */}
      {published !== false && (
        <FilterZone
          mode={inPlan ? 'plan' : 'all'}
          onModeChange={setMode}
          planSwims={planSwimsTotal}
          sessions={sessions}
          activeSessionKey={activeSession?.key ?? ''}
          onSelectSession={selectSession}
          shareUrl={shareUrl}
          orgCompIds={allOrgCompIds}
          onOpenSwimmer={openSwimmer}
          planSwimmerIds={planSwimmerIds}
          favSwimmers={favSwimmers}
          favClubs={favClubs}
          planSwimmers={planSwimmerChips}
          planClubs={planClubChips}
          onRemoveSwimmer={(id) => saveOwn({ ...shownPlan, swimmer_ids: shownPlan.swimmer_ids.filter((x) => x !== id) })}
          onRemoveClub={(id) => saveOwn({ ...shownPlan, club_ids: shownPlan.club_ids.filter((x) => x !== id) })}
          onEditPlan={openPicker}
        />
      )}

      {pickerOpen && (
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
          onShowPlan={() => { setPickerOpen(false); openPlan(); }}
          onClose={() => setPickerOpen(false)}
        />
      )}

      {state.zoom === 'plan' && (
        <PlanCard
          orgCompIds={allOrgCompIds}
          plan={shownPlan}
          swims={planSwims}
          activeOrgCompId={effectiveOrgCompId}
          activeDay={activeDayIso}
          loading={effective.loading || clubSwimsLoading}
          shared={sharedPlan != null}
          onChange={saveOwn}
          onOpenHeat={openHeatFromSwimmer}
        />
      )}
      {state.zoom === 'programme' && (
        <ProgrammeZoom
          orgCompId={effectiveOrgCompId}
          day={activeDayIso}
          followed={followed}
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
