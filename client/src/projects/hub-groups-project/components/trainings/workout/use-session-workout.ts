import { useCallback, useEffect, useRef, useState } from 'react';
import type { HubGroupLevel, LanePlan, Workout } from '../../../types';
import { lanePlansApi } from '../../lane-plans-api';
import type { AutosaveStatus } from '../../use-lane-autosave';

/**
 * Workout занятия на рабочем столе тренера (docs/plans/trainings-coach-flow-plan.md, §5.5, CF6):
 * загрузка с планом занятия, черновик и автосохранение (`PUT …/lane-plans/{sessionId}/workout`) — та же
 * механика, что у дорожек (`use-lane-autosave.ts`): запись через паузу после последней правки,
 * запросы по одному, уход дошлёт несохранённое, закрытие вкладки до записи — предупреждение.
 *
 * Workout живёт при плане: у занятия плана нет — `state: 'none'` (сначала «Create plan» в бассейне).
 * Ключ — id занятия `yyyy-MM-dd-HHmm` (CF5: план — у занятия, у двух занятий дня workout разный).
 */

export type SessionWorkoutState = 'loading' | 'none' | 'error' | 'ready';

export interface SessionWorkout {
  state: SessionWorkoutState;
  /** Черновик; workout ещё нет — пустой. */
  workout: Workout;
  setWorkout: (update: (w: Workout) => Workout) => void;
  /** План даты — уровни дорожек для превью «что получит дорожка». */
  plan: LanePlan | null;
  /** Уровни группы по рангу; null — грузятся / ошибка. */
  levels: HubGroupLevel[] | null;
  status: AutosaveStatus;
  error: string | null;
  retry: () => void;
  /** Дослать несохранённое; true — всё записано. */
  flush: () => Promise<boolean>;
  /** Перечитать (план создан / удалён); несохранённые правки не трогает. */
  reload: () => void;
}

const SAVE_DELAY_MS = 700;
const EMPTY: Workout = { note: null, sets: [] };

/** Ключ содержимого: пустой документ — `null` (на сервере это «workout нет»). */
const keyOf = (w: Workout) => (w.sets.length === 0 && !w.note?.trim() ? 'null' : JSON.stringify(w));

export function useSessionWorkout(groupId: number, sessionId: string | null): SessionWorkout {
  const [state, setState] = useState<SessionWorkoutState>('loading');
  const [workout, setDraft] = useState<Workout>(EMPTY);
  const [plan, setPlan] = useState<LanePlan | null>(null);
  const [levels, setLevels] = useState<HubGroupLevel[] | null>(null);
  const [status, setStatus] = useState<AutosaveStatus>('saved');
  const [error, setError] = useState<string | null>(null);
  const [nonce, setNonce] = useState(0);

  const latest = useRef(workout);
  latest.current = workout;
  const savedKey = useRef(keyOf(EMPTY));
  const running = useRef<Promise<boolean> | null>(null);
  const failed = useRef(false);
  const mounted = useRef(true);
  const dirty = keyOf(workout) !== savedKey.current;
  const dirtyRef = useRef(dirty);
  dirtyRef.current = dirty;

  useEffect(() => {
    let cancelled = false;
    lanePlansApi.levels(groupId).then((r) => {
      if (!cancelled && r.ok && r.data) setLevels([...r.data.levels].sort((a, b) => a.rank - b.rank));
    });
    return () => { cancelled = true; };
  }, [groupId]);

  useEffect(() => {
    if (!sessionId) return undefined;
    let cancelled = false;
    lanePlansApi.get(groupId, sessionId).then((r) => {
      if (cancelled) return;
      if (r.ok && r.data) {
        setPlan(r.data);
        // Тренер правит — свежая копия с сервера его черновик не перетирает.
        if (!dirtyRef.current) {
          const loaded = r.data.workout ?? EMPTY;
          savedKey.current = keyOf(loaded);
          setDraft(loaded);
        }
        setState('ready');
      } else {
        setPlan(null);
        setState(r.status === 404 ? 'none' : 'error');
      }
    });
    return () => { cancelled = true; };
  }, [groupId, sessionId, nonce]);

  const run = useCallback((): Promise<boolean> => {
    if (running.current) return running.current;
    const loop = async (): Promise<boolean> => {
      while (sessionId && keyOf(latest.current) !== savedKey.current) {
        const sent = latest.current;
        const key = keyOf(sent);
        if (mounted.current) { setStatus('saving'); setError(null); }
        const r = await lanePlansApi.saveWorkout(groupId, sessionId, key === 'null' ? null : sent);
        if (!r.ok) {
          failed.current = true;
          if (mounted.current) {
            setStatus('error');
            setError(r.status === 404
              ? 'The lane plan for this day was deleted. Create it again to keep the workout.'
              : r.error ?? 'Could not save the workout. Check the connection and try again.');
          }
          return false;
        }
        failed.current = false;
        savedKey.current = key;
      }
      if (mounted.current) setStatus('saved');
      return true;
    };
    const promise = loop().finally(() => { running.current = null; });
    running.current = promise;
    return promise;
  }, [groupId, sessionId]);

  // Правка → пауза → запись.
  const draftKey = keyOf(workout);
  useEffect(() => {
    if (draftKey === savedKey.current) {
      if (!running.current) { failed.current = false; setStatus('saved'); setError(null); }
      return undefined;
    }
    if (!running.current) setStatus((s) => (s === 'error' ? s : 'pending'));
    const timer = window.setTimeout(() => { void run(); }, SAVE_DELAY_MS);
    return () => window.clearTimeout(timer);
  }, [draftKey, run]);

  // Уход со стола: дослать ждавшее паузы (после ошибки — нет: тренер её видел).
  useEffect(() => {
    mounted.current = true;
    return () => {
      mounted.current = false;
      if (keyOf(latest.current) !== savedKey.current && !failed.current) void run();
    };
  }, [run]);

  const settled = status === 'saved';
  useEffect(() => {
    if (settled) return undefined;
    const warn = (e: BeforeUnloadEvent) => { e.preventDefault(); };
    window.addEventListener('beforeunload', warn);
    return () => window.removeEventListener('beforeunload', warn);
  }, [settled]);

  const setWorkout = useCallback((update: (w: Workout) => Workout) => setDraft((w) => update(w)), []);
  const retry = useCallback(() => { void run(); }, [run]);
  const reload = useCallback(() => setNonce((n) => n + 1), []);

  return { state, workout, setWorkout, plan, levels, status, error, retry, flush: run, reload };
}
