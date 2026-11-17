import { useCallback, useEffect, useRef, useState } from 'react';
import type { LanePlan, LanePlanInput } from '../types';
import { lanePlansApi } from './lane-plans-api';

/**
 * Автосохранение редактора дорожек (docs/plans/trainings-coach-flow-plan.md, CF3; решение «на
 * проверку тренеру»). Вся механика — здесь, редактор только отдаёт вход сохранения и его ключ:
 * вернуть явный Save = звать `flush()` с кнопки и поставить `delay` в `null`.
 *
 * - правка → через `delay` мс после ПОСЛЕДНЕГО изменения уходит `PUT`; пока идёт запрос, новые
 *   правки ждут и уходят следующим (всегда последнее состояние, запросы не обгоняют друг друга);
 * - вход не изменился (ключ равен сохранённому) — запроса нет;
 * - ошибка: статус `error`, правки остаются в редакторе; повтор — `retry()` или следующая правка;
 * - уход с экрана (размонтирование) дошлёт несохранённое; после ошибки не дошлёт — там тренер уже
 *   ответил на «Discard unsaved changes?»; `abandon` (план удалили) — тоже нет;
 * - закрытие вкладки, пока запись не дошла, — стандартное предупреждение браузера.
 */

export type AutosaveStatus = 'saved' | 'pending' | 'saving' | 'error';

export interface LaneAutosave {
  status: AutosaveStatus;
  /** Текст ошибки записи; null — ошибки нет. */
  error: string | null;
  /** Повторить запись сейчас. */
  retry: () => void;
  /** Дослать несохранённое и дождаться: сохранённый план либо null (записать не удалось / плана ещё нет). */
  flush: () => Promise<LanePlan | null>;
}

const SAVE_DELAY_MS = 700;

export function useLaneAutosave({
  groupId, planKey, draftKey, input, plan, savedKey: initialSavedKey, abandon, onSaved, delay = SAVE_DELAY_MS,
}: {
  groupId: number;
  /** Ключ занятия плана `yyyy-MM-dd-HHmm` (CF5) — адрес `PUT …/lane-plans/{planKey}`. */
  planKey: string;
  /** Ключ содержимого черновика (`inputKey` редактора). */
  draftKey: string;
  input: LanePlanInput;
  /** Сохранённый план на момент открытия редактора; null — плана ещё нет. */
  plan: LanePlan | null;
  /** Ключ сохранённого плана; null — плана нет, первый же черновик уходит на сервер. */
  savedKey: string | null;
  /** План удаляют — несохранённое при размонтировании не досылать. */
  abandon?: React.MutableRefObject<boolean>;
  onSaved: (plan: LanePlan) => void;
  /** Задержка после последней правки, мс; null — само не сохраняет (только `flush`). */
  delay?: number | null;
}): LaneAutosave {
  const latest = useRef({ key: draftKey, input });
  latest.current = { key: draftKey, input };
  const savedKey = useRef(initialSavedKey);
  const savedPlan = useRef(plan);
  const running = useRef<Promise<LanePlan | null> | null>(null);
  const mounted = useRef(true);
  const failed = useRef(false);
  const onSavedRef = useRef(onSaved);
  onSavedRef.current = onSaved;

  const [status, setStatus] = useState<AutosaveStatus>(initialSavedKey === draftKey ? 'saved' : 'pending');
  const [error, setError] = useState<string | null>(null);

  const run = useCallback((): Promise<LanePlan | null> => {
    if (running.current) return running.current;
    const loop = async (): Promise<LanePlan | null> => {
      while (latest.current.key !== savedKey.current) {
        const sent = latest.current;
        if (mounted.current) { setStatus('saving'); setError(null); }
        const result = await lanePlansApi.save(groupId, planKey, sent.input);
        if (!result.ok || !result.data) {
          failed.current = true;
          if (mounted.current) {
            setStatus('error');
            setError(result.error ?? 'Could not save. Check the connection and try again.');
          }
          return null;
        }
        failed.current = false;
        savedKey.current = sent.key;
        savedPlan.current = result.data;
        if (mounted.current) onSavedRef.current(result.data);
      }
      if (mounted.current) setStatus('saved');
      return savedPlan.current;
    };
    const promise = loop().finally(() => { running.current = null; });
    running.current = promise;
    return promise;
  }, [groupId, planKey]);

  // Правка: ждём паузу и пишем. Вернулся к сохранённому (Undo) — писать нечего.
  useEffect(() => {
    if (draftKey === savedKey.current) {
      if (!running.current) { failed.current = false; setStatus('saved'); setError(null); }
      return undefined;
    }
    if (!running.current) setStatus((s) => (s === 'error' ? s : 'pending'));
    if (delay == null) return undefined;
    const timer = window.setTimeout(() => { void run(); }, delay);
    return () => window.clearTimeout(timer);
  }, [draftKey, delay, run]);

  // Уход с экрана: дослать то, что ещё ждало паузы.
  useEffect(() => {
    mounted.current = true;
    return () => {
      mounted.current = false;
      if (latest.current.key !== savedKey.current && !failed.current && !abandon?.current) void run();
    };
  }, [run, abandon]);

  // Закрытие вкладки, пока запись не дошла.
  const settled = status === 'saved';
  useEffect(() => {
    if (settled) return undefined;
    const warn = (e: BeforeUnloadEvent) => { e.preventDefault(); };
    window.addEventListener('beforeunload', warn);
    return () => window.removeEventListener('beforeunload', warn);
  }, [settled]);

  const retry = useCallback(() => { void run(); }, [run]);

  return { status, error, retry, flush: run };
}
