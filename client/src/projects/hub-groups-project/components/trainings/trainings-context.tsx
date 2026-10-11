import React, { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState } from 'react';
import { DEEP_DESKTOP_QUERY, useMediaQuery } from '../../../../hooks/useMediaQuery';
import { readViewParam, writeViewParam } from '../../../components/deep/view-chips';
import { useTrainingSessions, type TrainingSessionsState } from '../../use-training-sessions';
import { useTrainingRsvp, type TrainingRsvpState } from '../../use-training-rsvp';
import type { HubGroupDetails, LanePlan, LanePlanSummary, RsvpAnswer, RsvpNote, TrainingRsvpPerson } from '../../types';
import ConfirmDialog from '../../../components/confirm-dialog/confirm-dialog';
import { lanePlansApi } from '../lane-plans-api';
import { useGroupBreaks } from '../use-group-breaks';
import { NEXT_ENTRIES_MAX, nextSessions } from './next/next-format';
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
}

/** `?view=` — старое `sessions` (до редизайна) читается как `next`: неизвестное → по умолчанию. */
const VIEWS: readonly TrainingsView[] = ['next', 'trainings', 'lanes'];
const DEFAULT_VIEW: TrainingsView = 'next';

export interface TrainingsTabState {
  group: HubGroupDetails;
  /** Управляющий группой (тот же гейт, что у таба Admin). Только прячет кнопки — права у сервера. */
  manages: boolean;
  /** ≥960px: без переключателя, две колонки (Next | Trainings), Lanes — во всю ширину. */
  isDesktop: boolean;
  view: TrainingsView;
  /**
   * Сменить вид. Пока в Lanes есть несохранённые правки редактора (`lanesDirty`), уход из Lanes
   * сначала спрашивает «Discard unsaved changes?» — одно место на «← Back to Next», сегменты и
   * любые `setView('next')` из секций.
   */
  setView: (view: TrainingsView) => void;
  /** В редакторе Lanes есть несохранённые правки; ставит сам редактор (через секцию), Т7. */
  lanesDirty: boolean;
  setLanesDirty: (dirty: boolean) => void;
  rsvp: TrainingRsvpState | null;
  /**
   * Ответы на ближайшие занятия (`nextSessions(group)`: три не отменённых и отменённые между ними,
   * Р9) — по индексу строки списка. Строка, чей ключ равен `group.next_training.id` (первое НЕ
   * отменённое), — тот же экземпляр, что `rsvp` (ответ сразу виден и в шапке страницы), остальные —
   * свои. Длина всегда `NEXT_ENTRIES_MAX`; `null` — строки нет или ответы не грузятся (гость).
   */
  upcoming: (TrainingRsvpState | null)[];
  /** Какое из ближайших занятий открыто у тренера (индекс строки `nextSessions`), вид `coach-next` (Т6). */
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
   * Полные планы дорожек ближайших занятий (РАЗНЫЕ даты `nextSessions(group)`): ключ —
   * yyyy-MM-dd. `null` — плана нет (404: участнику черновик тоже 404 — это не ошибка); ключа нет —
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
  group, manages, rsvp, openSchedule, onNextTrainingsChanged, onMarksChange, children,
}: {
  group: HubGroupDetails;
  manages: boolean;
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
  const applyView = useCallback((next: TrainingsView) => {
    setViewState(next);
    writeViewParam(next, DEFAULT_VIEW);
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
  /** Вид, в который ушли бы, но ждём ответа на «Discard unsaved changes?». */
  const [pendingView, setPendingView] = useState<TrainingsView | null>(null);
  const setView = useCallback((next: TrainingsView) => {
    if (dirtyRef.current && viewRef.current === 'lanes' && next !== 'lanes') {
      setPendingView(next);
      return;
    }
    applyView(next);
  }, [applyView]);

  const [sheet, setSheet] = useState<TrainingsSheet | null>(null);
  const closeSheet = useCallback(() => setSheet(null), []);

  const [rollIndexRaw, setRollIndex] = useState(0);

  // Слушатель изменений ответа — в ref: смена слушателя не должна перерисовывать весь таб.
  const rollListener = useRef<((change: RollChange) => void) | null>(null);
  const setRollChangeListener = useCallback((listener: ((change: RollChange) => void) | null) => {
    rollListener.current = listener;
  }, []);
  const onRollChange = useCallback((change: RollChange) => { rollListener.current?.(change); }, []);

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

  // Полные планы ближайших занятий (Т8): метки и число неразложенных. Даты — строкой-ключом,
  // чтобы эффект не перезапускался на каждый новый массив `nextSessions`.
  const planDatesKey = Array.from(new Set(nextSessions(group).map((s) => s.date))).join(',');
  const [upcomingPlans, setUpcomingPlans] = useState<Record<string, LanePlan | null>>({});
  useEffect(() => {
    if (!planDatesKey) return undefined;
    let cancelled = false;
    planDatesKey.split(',').forEach((date) => {
      lanePlansApi.get(group.id, date).then((res) => {
        if (cancelled) return;
        if (res.ok && res.data) setUpcomingPlans((prev) => ({ ...prev, [date]: res.data ?? null }));
        else if (res.status === 404) setUpcomingPlans((prev) => ({ ...prev, [date]: null }));
      });
    });
    return () => { cancelled = true; };
  }, [group.id, planDatesKey, lanePlansVersion]);

  // Метки внимания. Ближайшее занятие — `group.next_training` (ответы `rsvp`), не выбранное тренером.
  const nextDate = group.next_training?.date ?? null;
  const nextPlan = nextDate ? upcomingPlans[nextDate] ?? null : null;
  const [trainingsSeen, setTrainingsSeen] = useState(false);
  const [lanesSeen, setLanesSeen] = useState(false);
  const marks = useTrainingMarks({
    rsvp: r ?? null, manages, nextPlan, sessions: sessions.data, trainingsSeen, lanesSeen,
  });

  // Наверх — только посчитанное по пришедшим данным: пустые метки до ответа погасили бы таб группы.
  // План ближайшего занятия «пришёл», когда ключ даты есть (404 — тоже ответ, `null`).
  const marksReady = sessions.data != null && (nextDate == null || nextDate in upcomingPlans);
  useEffect(() => {
    if (marksReady) onMarksChange?.(marks);
  }, [marksReady, marks, onMarksChange]);

  // Гашение «нового» — один раз за монтирование таба (флаги в ref), не на каждое переключение видов.
  // Лента на экране: телефон — вид Trainings, десктоп — правая колонка (всё, кроме Lanes).
  const feedShown = isDesktop ? view !== 'lanes' : view === 'trainings';
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
  const upcomingIds = nextSessions(group);
  // Занятий стало меньше, чем выбранный индекс (прошло ближайшее) — не выходим за край.
  const rollIndex = Math.min(rollIndexRaw, Math.max(0, upcomingIds.length - 1));
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

  const value = useMemo<TrainingsTabState>(() => ({
    group, manages, isDesktop, view, setView, lanesDirty, setLanesDirty, rsvp: rsvp ?? null, upcoming,
    rollIndex, setRollIndex, lanePlans, reloadLanePlans, lanesHidden, openSchedule, reloadNextTrainings, upcomingPlans, markLanesShown,
    onRollChange, setRollChangeListener, sessions, breaks, marks, openSheet: setSheet, closeSheet, sheet,
  }), [
    group, manages, isDesktop, view, setView, lanesDirty, setLanesDirty, rsvp, upcoming, rollIndex, lanePlans, reloadLanePlans,
    lanesHidden, openSchedule, reloadNextTrainings, upcomingPlans, markLanesShown, onRollChange, setRollChangeListener, sessions,
    breaks, marks, closeSheet, sheet,
  ]);

  return (
    <TrainingsTabContext.Provider value={value}>
      {children}
      {pendingView && (
        <ConfirmDialog
          title="Discard unsaved changes?"
          confirmLabel="Discard"
          onConfirm={async () => {
            setLanesDirty(false);
            applyView(pendingView);
            setPendingView(null);
            return { success: true };
          }}
          onClose={() => setPendingView(null)}
        >
          <p className="m-0 mt-3 text-[13px] text-[var(--t-text-2)]">
            The lane plan you are editing has changes that are not saved yet.
          </p>
        </ConfirmDialog>
      )}
    </TrainingsTabContext.Provider>
  );
}
