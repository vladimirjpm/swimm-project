import React, { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState } from 'react';
import { DEEP_DESKTOP_QUERY, useMediaQuery } from '../../../../hooks/useMediaQuery';
import { readViewParam, writeViewParam } from '../../../components/deep/view-chips';
import { useTrainingSessions, type TrainingSessionsState } from '../../use-training-sessions';
import { useTrainingRsvp, type TrainingRsvpState } from '../../use-training-rsvp';
import type { HubGroupDetails, LaneChange, LanePlan, LanePlanSummary, NextTraining, RsvpAnswer, RsvpNote, TrainingRsvpPerson } from '../../types';
import ConfirmDialog from '../../../components/confirm-dialog/confirm-dialog';
import { lanePlansApi } from '../lane-plans-api';
import { useGroupBreaks } from '../use-group-breaks';
import { NEXT_ENTRIES_MAX, nextSessions } from './next/next-format';
import { usePastTrainings, type PastTrainingsState } from './past/use-past-trainings';
import type { TrainingsSheet } from './trainings-sheets';
import { goldMark, useTrainingMarks, type TrainingMarks } from './use-training-marks';

/**
 * Общие данные таба Trainings — ОДНО место на все секции (docs/plans/trainings-tab-redesign-plan.md
 * §3½). Секция берёт отсюда и ничего не грузит сама: новая ручка — новое поле здесь, а не
 * `fetch` в секции. Так любую секцию можно вынуть и поставить другую, не теряя загрузку, и
 * два блока одного экрана не спорят двумя копиями одних данных.
 *
 * Ответы на занятие (`rsvp`) приходят сверху: их же читают шапка и липкая полоса страницы
 * группы, экземпляр один. План дорожек и редактор грузит `GroupLanes` (Т7 оставил это там: дата, план и черновик
 * живут вместе); в контексте — список планов (`lanePlans`) для статуса «Draft / Published» вида
 * тренера, полные планы ближайших занятий (`upcomingPlans`, Т8) для меток и числа неразложенных,
 * и флаг несохранённых правок редактора (`lanesDirty`) для подтверждения ухода.
 *
 * Метки внимания (Т8) считаются здесь же и здесь же гасятся: `markSeen` ленты и Lanes вызывает
 * контекст (один раз за монтирование), секции о нём не знают.
 */

export type TrainingsView = 'next' | 'trainings' | 'lanes';

/** Ответ и заметка человека — до и после правки тренером (тост «Undo», шторка человека, Т6). */
export interface RollAnswerState {
  answer: RsvpAnswer | null;
  note: RsvpNote | null;
}

/** Изменение ответа тренером: переклички (`roll-call.tsx`) его слушает и показывает тост. */
export interface RollChange {
  person: TrainingRsvpPerson;
  prev: RollAnswerState;
  next: RollAnswerState;
  /** Ответ сдвинул пловца в плане дорожек (CF4): тост называет дорожку, Undo возвращает и её. */
  lane?: LaneChange | null;
}

/**
 * Команда редактору дорожек рабочего стола (CF4): ответ сдвинул пловца в плане на сервере —
 * повторить сдвиг в черновике (иначе автосохранение вернуло бы старое); «Needs you» и карточка
 * человека просят показать пловца или дорожку.
 */
export type PoolCommand =
  | { kind: 'lane-change'; change: LaneChange }
  | { kind: 'show-swimmer'; swimmerId: number }
  | { kind: 'show-lane'; laneNo: number }
  /** Закрепить / открепить пловца на его дорожке (карточка человека). */
  | { kind: 'pin'; swimmerId: number; on: boolean };

/** `?view=` — старое `sessions` (до редизайна) читается как `next`: неизвестное → по умолчанию. */
const VIEWS: readonly TrainingsView[] = ['next', 'trainings', 'lanes'];
const DEFAULT_VIEW: TrainingsView = 'next';

/**
 * `?session=<id>` — занятие, открытое на рабочем столе тренера (docs/plans/trainings-coach-flow-plan.md,
 * CF1). Ключ тот же, что у ответов (`NextTraining.id`). Нет параметра — список занятий.
 */
const SESSION_PARAM = 'session';
function readSessionParam(): string | null {
  return new URLSearchParams(window.location.search).get(SESSION_PARAM) || null;
}
function writeSessionParam(id: string | null): void {
  const url = new URL(window.location.href);
  if (id) url.searchParams.set(SESSION_PARAM, id);
  else url.searchParams.delete(SESSION_PARAM);
  window.history.replaceState(null, '', url.toString());
}

export interface TrainingsTabState {
  group: HubGroupDetails;
  /** Управляющий группой (тот же гейт, что у таба Admin). Только прячет кнопки — права у сервера. */
  manages: boolean;
  /**
   * Режим «👁 View» управляющего (страница группы): вид участника, а «своя» дорожка — выбранная в
   * ряду дорожек (`previewLane`), а не та, где стоит зритель. У участника всегда false.
   */
  lanePreview: boolean;
  /** Выбранная в превью дорожка; null — по умолчанию (`next/lane-preview.ts`). */
  previewLane: number | null;
  setPreviewLane: (laneNo: number) => void;
  /** ≥960px: без переключателя, две колонки (Next | Trainings), Lanes — во всю ширину. */
  isDesktop: boolean;
  view: TrainingsView;
  /**
   * Сменить вид. Пока в Lanes есть несохранённые правки редактора (`lanesDirty`), уход из Lanes
   * сначала спрашивает «Discard unsaved changes?» — одно место на «← Back to Next», сегменты и
   * любые `setView('next')` из секций.
   */
  setView: (view: TrainingsView) => void;
  /**
   * Открыть Lanes на плане занятия («✎ Edit lanes» / «Lanes ›» у бассейна): доска встаёт на план того
   * занятия, из которого пришли (ключ `yyyy-MM-dd-HHmm`, CF5), а не на план по умолчанию (`pickDefaultKey`).
   */
  openLanes: (sessionId: string) => void;
  /** Занятие, с которого открыт Lanes (`openLanes`); `null` — вход без него (сегмент участника, `?view=lanes`). */
  lanesKey: string | null;
  /**
   * В редакторе дорожек есть правки, которые НЕ УДАЛОСЬ сохранить (с CF3 редактор сохраняет сам);
   * ставит сам редактор (через секцию).
   */
  lanesDirty: boolean;
  setLanesDirty: (dirty: boolean) => void;
  /**
   * Занятие, открытое на рабочем столе тренера (`?session=<id>`, CF1 плана trainings-coach-flow);
   * `null` — тренер видит список занятий. У участника всегда `null`.
   */
  deskSession: NextTraining | null;
  /**
   * На столе ПРОШЕДШЕЕ занятие (CF9): открыто из списка «Past», среди ближайших его нет. Шапка
   * стола тогда без ‹ › и без Cancel / Restore (`past/past-session-head.tsx`).
   */
  deskIsPast: boolean;
  /**
   * Ответы выбранного занятия — того, что на столе (ближайшее или прошедшее), без стола —
   * `upcoming[rollIndex]`. Одно место для переклички, бассейна и шторок: `upcoming[rollIndex]`
   * прошедшего не знает.
   */
  selectedRoll: TrainingRsvpState | null;
  /** Прошедшие занятия с данными — список «Past» тренера (CF9); участнику `list` всегда `null`. */
  past: PastTrainingsState;
  /** Открыть рабочий стол занятия (кнопка Open карточки списка). */
  openDesk: (sessionId: string) => void;
  /** «← All trainings»: назад к списку. С несохранёнными правками плана — после «Discard?». */
  closeDesk: () => void;
  rsvp: TrainingRsvpState | null;
  /**
   * Ответы на ближайшие занятия (`nextSessions(group)`: три не отменённых и отменённые между ними,
   * Р9) — по индексу строки списка. Строка, чей ключ равен `group.next_training.id` (первое НЕ
   * отменённое), — тот же экземпляр, что `rsvp` (ответ сразу виден и в шапке страницы), остальные —
   * свои. Длина всегда `NEXT_ENTRIES_MAX`; `null` — строки нет или ответы не грузятся (гость).
   */
  upcoming: (TrainingRsvpState | null)[];
  /**
   * Какое из ближайших занятий открыто у тренера (индекс строки `nextSessions`). На рабочем столе
   * это индекс `deskSession`; `setRollIndex` (стрелки ‹ › `SessionNav`) переводит стол на другое
   * занятие и меняет `?session=` — с несохранёнными правками плана после «Discard?».
   */
  rollIndex: number;
  setRollIndex: (index: number) => void;
  /** Планы дорожек группы: тренеру — все, участнику — опубликованные (`null` — ещё не пришли). */
  lanePlans: LanePlanSummary[] | null;
  reloadLanePlans: () => void;
  /**
   * Сегмент Lanes скрыт (Т9): участнику нечего показывать — у группы нет расписания и ни одного
   * опубликованного плана. Пока список планов не пришёл, тоже скрыт (сегмент не мигает).
   * Тренеру Lanes виден всегда — он может создать план.
   */
  lanesHidden: boolean;
  /** «Set schedule» тренера — открыть Admin и прокрутить к расписанию; нет — кнопки нет. */
  openSchedule?: () => void;
  /**
   * Перечитать ближайшие занятия (`GET …/next-trainings`, Р9): тренер завёл / поправил / удалил
   * тренировку с галкой «Ask who's coming» или отменил / вернул занятие. Страница группы подменяет
   * `next_trainings` / `next_training`; без неё (проп не передан) — ничего не делает.
   */
  reloadNextTrainings: () => void;
  /**
   * Полные планы дорожек ближайших занятий (`nextSessions(group)`): ключ — id занятия
   * `yyyy-MM-dd-HHmm` (CF5: план — у занятия). `null` — плана нет (404: участнику черновик тоже 404 —
   * это не ошибка); ключа нет —
   * ещё не пришёл. Нужны меткам (черновик / неразложенные / «тебя перенесли») и числу
   * неразложенных в бассейне тренера. Перечитываются по `reloadLanePlans()`. Секции планы НЕ грузят.
   */
  upcomingPlans: Record<string, LanePlan | null>;
  /**
   * Доска Lanes участника получила план (или узнала, что его нет) — теперь можно гасить золотую
   * метку «тебя перенесли» на сервере. Зовёт `GroupLanes` через секцию; раньше — нельзя (см. контекст).
   */
  markLanesShown: () => void;
  /**
   * Тренер поменял чей-то ответ (шторка человека): сообщить переклички, чтобы она показала тост
   * «Undo». Без слушателя (переклички нет на экране) — ничего не делает.
   */
  onRollChange?: (change: RollChange) => void;
  /** Слушатель `onRollChange` — регистрирует `roll-call.tsx` (`null` при размонтировании). */
  setRollChangeListener: (listener: ((change: RollChange) => void) | null) => void;
  /** Передать команду редактору дорожек рабочего стола (CF4); редактора нет — ничего не делает. */
  sendPoolCommand: (command: PoolCommand) => void;
  /** Слушатель команд — регистрирует редактор дорожек (`null` при размонтировании). */
  setPoolListener: (listener: ((command: PoolCommand) => void) | null) => void;
  sessions: TrainingSessionsState;
  breaks: ReturnType<typeof useGroupBreaks>;
  marks: TrainingMarks;
  /** Открыть форму из реестра (`trainings-sheets.tsx`); секция не знает, как она выглядит. */
  openSheet: (sheet: TrainingsSheet) => void;
  closeSheet: () => void;
  sheet: TrainingsSheet | null;
}

const TrainingsTabContext = createContext<TrainingsTabState | null>(null);

export function useTrainingsTab(): TrainingsTabState {
  const state = useContext(TrainingsTabContext);
  if (!state) throw new Error('useTrainingsTab: секция таба Trainings вне TrainingsTabProvider');
  return state;
}

export function TrainingsTabProvider({
  group, manages, lanePreview = false, rsvp, openSchedule, onNextTrainingsChanged, onMarksChange, children,
}: {
  group: HubGroupDetails;
  manages: boolean;
  lanePreview?: boolean;
  rsvp?: TrainingRsvpState | null;
  openSchedule?: () => void;
  onNextTrainingsChanged?: () => void;
  /**
   * Метки видов — наверх, для таба группы: пока таб открыт, гашение «увидел» и правки плана видны
   * на табе сразу, без перечитывания. Зовётся, только когда лента и план ближайшего занятия пришли.
   */
  onMarksChange?: (marks: TrainingMarks) => void;
  children: React.ReactNode;
}) {
  const reloadNextTrainings = useCallback(() => { onNextTrainingsChanged?.(); }, [onNextTrainingsChanged]);
  const isDesktop = useMediaQuery(DEEP_DESKTOP_QUERY);
  const [view, setViewState] = useState<TrainingsView>(() => readViewParam(VIEWS, DEFAULT_VIEW));
  const [lanesKey, setLanesKey] = useState<string | null>(null);
  const applyView = useCallback((next: TrainingsView) => {
    setViewState(next);
    writeViewParam(next, DEFAULT_VIEW);
    // Ушли из Lanes — занятие отработало: следующий вход без него (сегмент) берёт план по умолчанию.
    if (next !== 'lanes') setLanesKey(null);
  }, []);

  // Несохранённые правки редактора Lanes (Т7): флаг дублируется в ref, чтобы `setView` оставался
  // стабильной функцией, а не пересоздавался на каждую правку.
  const [lanesDirty, setLanesDirtyState] = useState(false);
  const dirtyRef = useRef(false);
  const viewRef = useRef(view);
  viewRef.current = view;
  const setLanesDirty = useCallback((dirty: boolean) => {
    dirtyRef.current = dirty;
    setLanesDirtyState(dirty);
  }, []);
  /** Переход, который сделали бы, но ждём ответа на «Discard unsaved changes?». */
  const [pendingLeave, setPendingLeave] = useState<{ run: () => void } | null>(null);
  const setView = useCallback((next: TrainingsView) => {
    if (dirtyRef.current && viewRef.current === 'lanes' && next !== 'lanes') {
      setPendingLeave({ run: () => applyView(next) });
      return;
    }
    applyView(next);
  }, [applyView]);

  // Рабочий стол тренера (CF1): открытое занятие — в адресе. Уход со стола и смена занятия с
  // несохранёнными правками плана спрашивают «Discard?» (тот же флаг `lanesDirty`).
  const upcomingIds = nextSessions(group);
  const [deskId, setDeskId] = useState<string | null>(readSessionParam);
  const applyDesk = useCallback((id: string | null) => {
    setDeskId(id);
    writeSessionParam(id);
  }, []);
  const leaveDesk = useCallback((id: string | null) => {
    if (dirtyRef.current) setPendingLeave({ run: () => applyDesk(id) });
    else applyDesk(id);
  }, [applyDesk]);
  const openDesk = useCallback((id: string) => leaveDesk(id), [leaveDesk]);
  const closeDesk = useCallback(() => leaveDesk(null), [leaveDesk]);
  const deskIndex = manages && deskId ? upcomingIds.findIndex((s) => s.id === deskId) : -1;
  // Не среди ближайших — может быть прошедшим из списка «Past» (CF9).
  const past = usePastTrainings(group.id, manages);
  const pastDesk = manages && deskId && deskIndex < 0
    ? past.list?.find((p) => p.session.id === deskId)?.session ?? null
    : null;
  const deskSession = deskIndex >= 0 ? upcomingIds[deskIndex] : pastDesk;
  const deskIsPast = deskIndex < 0 && deskSession != null;
  // Занятия из адреса нет ни среди ближайших, ни среди прошедших (удалено, приватный список ещё не
  // пришёл) — `deskSession` пуст и тренер видит список. Параметр при этом НЕ снимаем: общий ответ
  // страницы приходит раньше приватного списка с разовыми занятиями, и диплинк на такое занятие
  // стёрся бы до того, как оно появится (поймано на проверке CF1).
  // Вернулись со стола к списку — счётчики «Past» могли поменяться (поправил ответы задним числом).
  const { reload: reloadPast } = past;
  const deskShown = deskSession != null;
  const wasDeskShown = useRef(deskShown);
  useEffect(() => {
    if (wasDeskShown.current && !deskShown) reloadPast();
    wasDeskShown.current = deskShown;
  }, [deskShown, reloadPast]);
  // У тренера видов Next / Trainings / Lanes нет: старые `?view=` ведут на список.
  useEffect(() => {
    if (manages && view !== DEFAULT_VIEW) applyView(DEFAULT_VIEW);
  }, [manages, view, applyView]);
  const openLanes = useCallback((sessionId: string) => {
    setLanesKey(sessionId);
    applyView('lanes');
  }, [applyView]);

  const [previewLane, setPreviewLane] = useState<number | null>(null);

  const [sheet, setSheet] = useState<TrainingsSheet | null>(null);
  const closeSheet = useCallback(() => setSheet(null), []);

  const [rollIndexRaw, setRollIndexRaw] = useState(0);

  // Слушатель изменений ответа — в ref: смена слушателя не должна перерисовывать весь таб.
  const rollListener = useRef<((change: RollChange) => void) | null>(null);
  const setRollChangeListener = useCallback((listener: ((change: RollChange) => void) | null) => {
    rollListener.current = listener;
  }, []);
  const onRollChange = useCallback((change: RollChange) => { rollListener.current?.(change); }, []);
  const poolListener = useRef<((command: PoolCommand) => void) | null>(null);
  const setPoolListener = useCallback((listener: ((command: PoolCommand) => void) | null) => {
    poolListener.current = listener;
  }, []);
  const sendPoolCommand = useCallback((command: PoolCommand) => { poolListener.current?.(command); }, []);

  // Планы дорожек: тренеру — статус «Draft / Published» на дату занятия; участнику (сервер отдаёт
  // ему только опубликованные) — чтобы знать, есть ли что показать во вкладке Lanes (`lanesHidden`).
  const [lanePlans, setLanePlans] = useState<LanePlanSummary[] | null>(null);
  const [lanePlansVersion, setLanePlansVersion] = useState(0);
  const reloadLanePlans = useCallback(() => setLanePlansVersion((v) => v + 1), []);
  useEffect(() => {
    let cancelled = false;
    lanePlansApi.list(group.id).then((res) => {
      if (!cancelled && res.ok && res.data) setLanePlans(res.data);
    });
    return () => { cancelled = true; };
  }, [group.id, lanePlansVersion]);
  const lanesHidden = !manages && !group.training_schedule && (lanePlans == null || lanePlans.length === 0);
  // Пришли по `?view=lanes`, а вкладка скрыта — на Next (правок у участника нет, подтверждение не нужно).
  // Только когда список планов ПРИШЁЛ: пока он грузится, сегмент скрыт, но диплинк на опубликованный
  // план группы без расписания откатывать рано.
  useEffect(() => {
    if (lanesHidden && lanePlans != null && view === 'lanes') applyView('next');
  }, [lanesHidden, lanePlans, view, applyView]);

  // Таб рисуется только своим (у остальных замок) — список тренировок можно грузить сразу.
  const sessions = useTrainingSessions(group.id, true);
  const r = rsvp?.rsvp;
  // Перерыв меняет знаменатель и бассейн — после его правки ответы перечитываются.
  const breaks = useGroupBreaks(group.id, !!r && (r.is_member || r.can_manage), rsvp?.reload);

  // Полные планы ближайших занятий (Т8): метки и число неразложенных. Ключ — id занятия (CF5),
  // строкой, чтобы эффект не перезапускался на каждый новый массив `nextSessions`.
  // Прошедшее на столе (CF9) — его план тоже нужен шторке человека и меткам бассейна.
  const planKeysKey = Array.from(new Set([
    ...nextSessions(group).map((s) => s.id).filter((id): id is string => !!id),
    ...(deskIsPast && deskSession?.id ? [deskSession.id] : []),
  ])).join(',');
  const [upcomingPlans, setUpcomingPlans] = useState<Record<string, LanePlan | null>>({});
  useEffect(() => {
    if (!planKeysKey) return undefined;
    let cancelled = false;
    planKeysKey.split(',').forEach((key) => {
      lanePlansApi.get(group.id, key).then((res) => {
        if (cancelled) return;
        if (res.ok && res.data) setUpcomingPlans((prev) => ({ ...prev, [key]: res.data ?? null }));
        else if (res.status === 404) setUpcomingPlans((prev) => ({ ...prev, [key]: null }));
      });
    });
    return () => { cancelled = true; };
  }, [group.id, planKeysKey, lanePlansVersion]);

  // Метки внимания. Ближайшее занятие — `group.next_training` (ответы `rsvp`), не выбранное тренером.
  const nextKey = group.next_training?.id ?? null;
  const nextPlan = nextKey ? upcomingPlans[nextKey] ?? null : null;
  const [trainingsSeen, setTrainingsSeen] = useState(false);
  const [lanesSeen, setLanesSeen] = useState(false);
  const marks = useTrainingMarks({
    rsvp: r ?? null, manages, nextPlan, sessions: sessions.data, trainingsSeen, lanesSeen,
  });

  // Наверх — только посчитанное по пришедшим данным: пустые метки до ответа погасили бы таб группы.
  // План ближайшего занятия «пришёл», когда ключ даты есть (404 — тоже ответ, `null`).
  const marksReady = sessions.data != null && (nextKey == null || nextKey in upcomingPlans);
  useEffect(() => {
    if (marksReady) onMarksChange?.(marks);
  }, [marksReady, marks, onMarksChange]);

  // Гашение «нового» — один раз за монтирование таба (флаги в ref), не на каждое переключение видов.
  // Лента на экране: телефон — вид Trainings, десктоп — правая колонка (всё, кроме Lanes).
  // Тренеру лента стоит под списком занятий (CF1) — видна, пока не открыт рабочий стол.
  const feedShown = manages ? deskSession == null : isDesktop ? view !== 'lanes' : view === 'trainings';
  const newMedia = sessions.data?.new_media_count ?? 0;
  const feedSeenSent = useRef(false);
  const { markSeen: markFeedSeen } = sessions;
  useEffect(() => {
    if (!feedShown || newMedia <= 0 || feedSeenSent.current) return;
    feedSeenSent.current = true;
    setTrainingsSeen(true);
    void markFeedSeen();
  }, [feedShown, newMedia, markFeedSeen]);
  // Участнику Lanes гасит золотую «тебя перенесли»; сама заметка на доске остаётся (план уже загружен).
  // Тренеру не гасит ничего: его метка держится, пока план черновик / есть неразложенные.
  // ⚠ Только ПОСЛЕ того, как доска получила свой план (`markLanesShown` из `GroupLanes`): `PUT
  // …/lane-plans/seen` гасит `moved_from_lane` на сервере, и уйди он раньше `GET` доски — заметка
  // «Coach moved you…» не показалась бы ни разу (доска сперва грузит список планов, потом план).
  const memberGoldLit = !manages && goldMark(nextPlan, false) != null;
  const [lanesShown, setLanesShown] = useState(false);
  const markLanesShown = useCallback(() => setLanesShown(true), []);
  const lanesSeenSent = useRef(false);
  useEffect(() => {
    if (view !== 'lanes' || !lanesShown || !memberGoldLit || lanesSeenSent.current) return;
    lanesSeenSent.current = true;
    setLanesSeen(true);
    void lanePlansApi.markSeen(group.id);
  }, [view, lanesShown, memberGoldLit, group.id]);

  // Ответы на ближайшие занятия: хуки вызываются ВСЕГДА и в одном порядке (sessionId undefined — нет
  // занятия). Строк до `NEXT_ENTRIES_MAX`: три не отменённых и отменённые между ними (Р9), поэтому
  // первое НЕ отменённое занятие (`group.next_training`) — не обязательно строка 0. Его ответы уже
  // грузит страница группы (`rsvp`) — своему слоту хука id не даём, чтобы не читать дважды.
  // Занятий стало меньше, чем выбранный индекс (прошло ближайшее) — не выходим за край.
  const rollIndex = deskIndex >= 0 ? deskIndex : Math.min(rollIndexRaw, Math.max(0, upcomingIds.length - 1));
  const idsKey = upcomingIds.map((s) => s.id ?? '').join(',');
  const deskOpen = deskSession != null;
  const setRollIndex = useCallback((index: number) => {
    const id = idsKey.split(',')[index];
    if (deskOpen && id) leaveDesk(id);
    else setRollIndexRaw(index);
  }, [idsKey, deskOpen, leaveDesk]);
  const answersEnabled = rsvp?.rsvp != null;
  const sharedId = group.next_training?.id;
  const slotId = (i: number) => (upcomingIds[i]?.id && upcomingIds[i].id !== sharedId ? upcomingIds[i].id : undefined);
  const slot0 = useTrainingRsvp(group.id, slotId(0), answersEnabled);
  const slot1 = useTrainingRsvp(group.id, slotId(1), answersEnabled);
  const slot2 = useTrainingRsvp(group.id, slotId(2), answersEnabled);
  const slot3 = useTrainingRsvp(group.id, slotId(3), answersEnabled);
  const slot4 = useTrainingRsvp(group.id, slotId(4), answersEnabled);
  const slot5 = useTrainingRsvp(group.id, slotId(5), answersEnabled);
  const upcoming = useMemo<(TrainingRsvpState | null)[]>(() => {
    const slots = [slot0, slot1, slot2, slot3, slot4, slot5];
    return Array.from({ length: NEXT_ENTRIES_MAX }, (_, i) => {
      const id = upcomingIds[i]?.id;
      if (!id) return null;
      return id === sharedId ? rsvp ?? null : slots[i];
    });
    // eslint-disable-next-line react-hooks/exhaustive-deps -- `upcomingIds` пересоздаётся на каждый рендер; ключ — состав id
  }, [rsvp, slot0, slot1, slot2, slot3, slot4, slot5, sharedId, upcomingIds.map((s) => s.id).join(',')]);
  // Ответы прошедшего на столе (CF9) — свой экземпляр: среди слотов ближайших его нет.
  const pastRoll = useTrainingRsvp(group.id, deskIsPast ? deskSession?.id : undefined, manages);
  const selectedRoll = deskIsPast ? pastRoll : upcoming[rollIndex] ?? null;

  const value = useMemo<TrainingsTabState>(() => ({
    group, manages, lanePreview, previewLane, setPreviewLane, isDesktop, view, setView, openLanes, lanesKey, lanesDirty, setLanesDirty, rsvp: rsvp ?? null, upcoming,
    deskSession, deskIsPast, selectedRoll, past, openDesk, closeDesk,
    rollIndex, setRollIndex, lanePlans, reloadLanePlans, lanesHidden, openSchedule, reloadNextTrainings, upcomingPlans, markLanesShown,
    onRollChange, setRollChangeListener, sendPoolCommand, setPoolListener, sessions, breaks, marks, openSheet: setSheet, closeSheet, sheet,
  }), [
    group, manages, lanePreview, previewLane, isDesktop, view, setView, openLanes, lanesKey, lanesDirty, setLanesDirty, rsvp, upcoming, rollIndex, setRollIndex,
    deskSession, deskIsPast, selectedRoll, past, openDesk, closeDesk, lanePlans, reloadLanePlans, lanesHidden, openSchedule, reloadNextTrainings, upcomingPlans, markLanesShown, onRollChange, setRollChangeListener, sendPoolCommand, setPoolListener, sessions,
    breaks, marks, closeSheet, sheet,
  ]);

  return (
    <TrainingsTabContext.Provider value={value}>
      {children}
      {pendingLeave && (
        <ConfirmDialog
          title="Discard unsaved changes?"
          confirmLabel="Discard"
          onConfirm={async () => {
            setLanesDirty(false);
            pendingLeave.run();
            setPendingLeave(null);
            return { success: true };
          }}
          onClose={() => setPendingLeave(null)}
        >
          <p className="m-0 mt-3 text-[13px] text-[var(--t-text-2)]">
            The last changes to the lane plan could not be saved. Leave without them?
          </p>
        </ConfirmDialog>
      )}
    </TrainingsTabContext.Provider>
  );
}
