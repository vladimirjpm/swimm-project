import { useCallback, useEffect, useState } from 'react';
import { publicationsApiFetch } from './components/group-bits';
import type {
  TrainingSession, TrainingSessionInput, TrainingSessionList, TrainingSessionMediaInput, TrainingSessionSets,
} from './types';

/**
 * Тренировки группы как записи и ссылки к ним (docs/plans/group-events-media-plan.md, Е3).
 * `GET /api/hub-groups/{id}/sessions` — личный ответ (управляющим и активным участникам, без
 * кэша); мутации — те же адреса, антифорджери через `publicationsApiFetch`.
 *
 * После каждой записи список перечитывается целиком: он маленький, а флаги «моё / могу
 * удалить» и потолки считает сервер — оптимистичная копия здесь врала бы.
 */

export interface TrainingSessionsState {
  data: TrainingSessionList | null;
  loading: boolean;
  /** Ошибка загрузки списка (не записи). */
  loadError: string | null;
  busy: boolean;
  /** Завести тренировку; null — успех, иначе текст отказа. */
  create: (input: TrainingSessionInput) => Promise<string | null>;
  update: (id: number, input: TrainingSessionInput) => Promise<string | null>;
  remove: (id: number) => Promise<string | null>;
  addMedia: (sessionId: number, input: TrainingSessionMediaInput) => Promise<string | null>;
  removeMedia: (sessionId: number, mediaId: number) => Promise<string | null>;
  /** «Who's in it»: сменить / снять (null) метку пловца у ссылки — только управляющий. После записи список перечитывается. */
  setMediaSwimmer: (sessionId: number, mediaId: number, swimmerId: number | null) => Promise<string | null>;
  /** ★ ключевого сета (Р4), только управляющему; null снимает. После записи список перечитывается. */
  setKeySet: (sessionId: number, setNo: number | null) => Promise<string | null>;
  /** «Sets & times» тренировки — лениво, при раскрытии (не кэшируется здесь). */
  loadSets: (sessionId: number) => Promise<TrainingSessionSets | null>;
  /**
   * Зритель открыл ленту Trainings (Т1б): гаснет «новые видео». Список НЕ перечитывается — метка
   * на открытой ленте остаётся до следующей загрузки, чтобы «NEW» было видно, пока смотрят.
   */
  markSeen: () => Promise<void>;
}

async function errorOf(r: Response, fallback: string): Promise<string> {
  const payload = await r.json().catch(() => null) as { error?: string } | null;
  if (r.status === 403) return payload?.error ?? 'You can’t do this in this group.';
  return payload?.error ?? fallback;
}

export function useTrainingSessions(groupId: number | null, enabled: boolean): TrainingSessionsState {
  const [data, setData] = useState<TrainingSessionList | null>(null);
  const [loading, setLoading] = useState(false);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [version, setVersion] = useState(0);

  const base = groupId != null ? `/api/hub-groups/${groupId}/sessions` : null;

  useEffect(() => {
    if (!enabled || !base) { setData(null); return undefined; }
    let cancelled = false;
    setLoading(true);
    publicationsApiFetch(base, { cache: 'no-store' })
      .then(async (r) => {
        if (cancelled) return;
        if (r.ok) { setData(await r.json() as TrainingSessionList); setLoadError(null); }
        else setLoadError(`Trainings unavailable (${r.status})`);
      })
      .catch(() => { if (!cancelled) setLoadError('Trainings unavailable'); })
      .finally(() => { if (!cancelled) setLoading(false); });
    return () => { cancelled = true; };
  }, [enabled, base, version]);

  const write = useCallback(async (url: string, method: string, body: unknown, fallback: string) => {
    if (!base) return 'Group not found.';
    setBusy(true);
    try {
      const r = await publicationsApiFetch(url, {
        method,
        body: body === undefined ? undefined : JSON.stringify(body),
      });
      if (!r.ok) return await errorOf(r, fallback);
      setVersion((v) => v + 1);
      return null;
    } catch {
      return fallback;
    } finally {
      setBusy(false);
    }
  }, [base]);

  const create = useCallback((input: TrainingSessionInput) =>
    write(base!, 'POST', input, 'Could not create the training.'), [write, base]);
  const update = useCallback((id: number, input: TrainingSessionInput) =>
    write(`${base}/${id}`, 'PUT', input, 'Could not save the training.'), [write, base]);
  const remove = useCallback((id: number) =>
    write(`${base}/${id}`, 'DELETE', undefined, 'Could not delete the training.'), [write, base]);
  const addMedia = useCallback((sessionId: number, input: TrainingSessionMediaInput) =>
    write(`${base}/${sessionId}/media`, 'POST', input, 'Could not add the link.'), [write, base]);
  const removeMedia = useCallback((sessionId: number, mediaId: number) =>
    write(`${base}/${sessionId}/media/${mediaId}`, 'DELETE', undefined, 'Could not remove the link.'), [write, base]);
  const setMediaSwimmer = useCallback((sessionId: number, mediaId: number, swimmerId: number | null) =>
    write(`${base}/${sessionId}/media/${mediaId}/swimmer`, 'PUT', { swimmer_id: swimmerId },
      'Could not change who’s in it.'), [write, base]);
  const setKeySet = useCallback((sessionId: number, setNo: number | null) =>
    write(`${base}/${sessionId}/key-set`, 'PUT', { set_no: setNo }, 'Could not mark the set.'), [write, base]);

  const loadSets = useCallback(async (sessionId: number) => {
    if (!base) return null;
    try {
      const r = await publicationsApiFetch(`${base}/${sessionId}/sets`, { cache: 'no-store' });
      return r.ok ? await r.json() as TrainingSessionSets : null;
    } catch {
      return null;
    }
  }, [base]);

  const markSeen = useCallback(async () => {
    if (!base) return;
    await publicationsApiFetch(`${base}/seen`, { method: 'PUT' }).catch(() => undefined);
  }, [base]);

  return {
    data, loading, loadError, busy, create, update, remove, addMedia, removeMedia, setMediaSwimmer, setKeySet, loadSets, markSeen,
  };
}

/** Подпись тренировки: название тренера, иначе план из источника, иначе «Training». */
export function sessionLabel(s: Pick<TrainingSession, 'title' | 'plan'>): string {
  return s.title?.trim() || s.plan?.trim() || 'Training';
}
