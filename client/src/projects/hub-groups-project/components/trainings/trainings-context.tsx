import React, { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState } from 'react';
import { DEEP_DESKTOP_QUERY, useMediaQuery } from '../../../../hooks/useMediaQuery';
import { readViewParam, writeViewParam } from '../../../components/deep/view-chips';
import { useTrainingSessions, type TrainingSessionsState } from '../../use-training-sessions';
import { useTrainingRsvp, type TrainingRsvpState } from '../../use-training-rsvp';
import type { HubGroupDetails, LanePlanSummary, RsvpAnswer, RsvpNote, TrainingRsvpPerson } from '../../types';
import ConfirmDialog from '../../../components/confirm-dialog/confirm-dialog';
import { lanePlansApi } from '../lane-plans-api';
import { useGroupBreaks } from '../use-group-breaks';
import { nextSessions } from './next/next-format';
import type { TrainingsSheet } from './trainings-sheets';
import { useTrainingMarks, type TrainingMarks } from './use-training-marks';

/**
 * Общие данные таба Trainings — ОДНО место на все секции (docs/plans/trainings-tab-redesign-plan.md
 * §3½). Секция берёт отсюда и ничего не грузит сама: новая ручка — новое поле здесь, а не
 * `fetch` в секции. Так любую секцию можно вынуть и поставить другую, не теряя загрузку, и
 * два блока одного экрана не спорят двумя копиями одних данных.
 *
 * Ответы на занятие (`rsvp`) приходят сверху: их же читают шапка и липкая полоса страницы
 * группы, экземпляр один. План дорожек и редактор грузит `GroupLanes` (Т7 оставил это там: дата, план и черновик
 * живут вместе); в контексте — список планов (`lanePlans`) для статуса «Draft / Published» вида
 * тренера и флаг несохранённых правок редактора (`lanesDirty`) для подтверждения ухода.
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
   * Ответы на три ближайших занятия (`group.next_trainings`): `[0]` — тот же экземпляр, что `rsvp`
   * (ответ сразу виден и в шапке страницы), `[1]`/`[2]` — свои. Длина всегда 3; `null` — занятия
   * нет или ответы не грузятся (гость).
   */
  upcoming: (TrainingRsvpState | null)[];
  /** Какое из трёх ближайших занятий открыто у тренера (0..2), вид `coach-next` (Т6). */
  rollIndex: number;
  setRollIndex: (index: number) => void;
  /** Планы дорожек группы (только `manages`; `null` — ещё не пришли или не управляющий). */
  lanePlans: LanePlanSummary[] | null;
  reloadLanePlans: () => void;
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
  group, manages, rsvp, children,
}: {
  group: HubGroupDetails;
  manages: boolean;
  rsvp?: TrainingRsvpState | null;
  children: React.ReactNode;
}) {
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

  // Планы дорожек — статус «Draft / Published» на дату занятия (только управляющему).
  const [lanePlans, setLanePlans] = useState<LanePlanSummary[] | null>(null);
  const [lanePlansVersion, setLanePlansVersion] = useState(0);
  const reloadLanePlans = useCallback(() => setLanePlansVersion((v) => v + 1), []);
  useEffect(() => {
    if (!manages) { setLanePlans(null); return undefined; }
    let cancelled = false;
    lanePlansApi.list(group.id).then((res) => {
      if (!cancelled && res.ok && res.data) setLanePlans(res.data);
    });
    return () => { cancelled = true; };
  }, [group.id, manages, lanePlansVersion]);

  // Таб рисуется только своим (у остальных замок) — список тренировок можно грузить сразу.
  const sessions = useTrainingSessions(group.id, true);
  const r = rsvp?.rsvp;
  // Перерыв меняет знаменатель и бассейн — после его правки ответы перечитываются.
  const breaks = useGroupBreaks(group.id, !!r && (r.is_member || r.can_manage), rsvp?.reload);
  const marks = useTrainingMarks();

  // Ответы на 2-е и 3-е занятие: хуки вызываются ВСЕГДА и в одном порядке (sessionId undefined — нет занятия).
  const upcomingIds = nextSessions(group);
  // Занятий стало меньше, чем выбранный индекс (прошло ближайшее) — не выходим за край.
  const rollIndex = Math.min(rollIndexRaw, Math.max(0, upcomingIds.length - 1));
  const answersEnabled = rsvp?.rsvp != null;
  const second = useTrainingRsvp(group.id, upcomingIds[1]?.id, answersEnabled);
  const third = useTrainingRsvp(group.id, upcomingIds[2]?.id, answersEnabled);
  const has2 = !!upcomingIds[1]?.id;
  const has3 = !!upcomingIds[2]?.id;
  const upcoming = useMemo<(TrainingRsvpState | null)[]>(
    () => [rsvp ?? null, has2 ? second : null, has3 ? third : null],
    [rsvp, second, third, has2, has3],
  );

  const value = useMemo<TrainingsTabState>(() => ({
    group, manages, isDesktop, view, setView, lanesDirty, setLanesDirty, rsvp: rsvp ?? null, upcoming,
    rollIndex, setRollIndex, lanePlans, reloadLanePlans, onRollChange, setRollChangeListener,
    sessions, breaks, marks, openSheet: setSheet, closeSheet, sheet,
  }), [
    group, manages, isDesktop, view, setView, lanesDirty, setLanesDirty, rsvp, upcoming, rollIndex, lanePlans, reloadLanePlans,
    onRollChange, setRollChangeListener, sessions, breaks, marks, closeSheet, sheet,
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
