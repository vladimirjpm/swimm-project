import { useCallback, useEffect, useRef, useState } from 'react';
import { publicationsApiFetch } from './components/group-bits';
import type { RsvpAnswer, RsvpNote, TrainingRsvp } from './types';

/**
 * Ответы «иду / не уверен / не приду» на одно занятие (docs/plans/entity-hero-roles-plan.md, Ш2).
 *
 * ОДИН экземпляр на страницу группы (как членство): из него читают шапка, липкая полоса и
 * таб Trainings — второй экземпляр после клика показывал бы в полосе старое.
 *
 * Ответ ОПТИМИСТИЧНЫЙ (хендофф §4): кнопка закрашивается и счётчики меняются сразу, потом
 * приходит ответ сервера и заменяет всё целиком. Отказ — откат к тому, что было, и текст
 * причины в `error`.
 *
 * Данные личные: запрос только участнику и управляющему (`enabled`), ответ без кэша.
 */

export interface TrainingRsvpState {
  rsvp: TrainingRsvp | null;
  loading: boolean;
  error: string | null;
  /** Свой ответ; повторный тап той же кнопки вызывающий передаёт как `null` (снять). */
  answer: (answer: RsvpAnswer | null, note?: RsvpNote | null) => Promise<void>;
  /** Управляющий ставит ответ за участника (попросил в WhatsApp). */
  answerFor: (userId: number, answer: RsvpAnswer | null) => Promise<void>;
  /** Перечитать с сервера — после того, что меняет ответы со стороны (перерыв, Ш3.1). */
  reload: () => void;
}

const COUNT_KEY: Record<RsvpAnswer, 'yes' | 'maybe' | 'no'> = { yes: 'yes', maybe: 'maybe', no: 'no' };

/** Счётчики после смены одного ответа `from` → `to` (для оптимистичного вида). */
function shiftCounts(r: TrainingRsvp, from: RsvpAnswer | null, to: RsvpAnswer | null): TrainingRsvp {
  const next = { ...r };
  if (from) next[COUNT_KEY[from]] = Math.max(0, next[COUNT_KEY[from]] - 1);
  if (to) next[COUNT_KEY[to]] += 1;
  return next;
}

export function useTrainingRsvp(groupId: number | null, sessionId: string | null | undefined, enabled: boolean): TrainingRsvpState {
  const [rsvp, setRsvp] = useState<TrainingRsvp | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  // Последнее подтверждённое сервером состояние — к нему откатываемся при отказе.
  const confirmed = useRef<TrainingRsvp | null>(null);
  const [version, setVersion] = useState(0);
  const reload = useCallback(() => setVersion((v) => v + 1), []);

  const url = groupId != null && sessionId ? `/api/hub-groups/${groupId}/rsvp/${encodeURIComponent(sessionId)}` : null;

  useEffect(() => {
    if (!enabled || !url) { setRsvp(null); confirmed.current = null; return undefined; }
    let cancelled = false;
    setLoading(true);
    publicationsApiFetch(url, { cache: 'no-store' })
      .then((r) => (r.ok ? (r.json() as Promise<TrainingRsvp>) : null))
      .then((data) => {
        if (cancelled) return;
        setRsvp(data);
        confirmed.current = data;
      })
      .catch(() => { if (!cancelled) setRsvp(null); })
      .finally(() => { if (!cancelled) setLoading(false); });
    return () => { cancelled = true; };
  }, [enabled, url, version]);

  const send = useCallback(async (optimistic: TrainingRsvp, body: object) => {
    if (!url) return;
    setError(null);
    setRsvp(optimistic);
    try {
      const r = await publicationsApiFetch(url, { method: 'PUT', body: JSON.stringify(body) });
      if (r.ok) {
        const data = (await r.json()) as TrainingRsvp;
        confirmed.current = data;
        setRsvp(data);
        return;
      }
      const payload = await r.json().catch(() => null);
      setError(payload?.error ?? 'Could not save your answer.');
    } catch {
      setError('Could not save your answer.');
    }
    setRsvp(confirmed.current);
  }, [url]);

  const answer = useCallback(async (next: RsvpAnswer | null, note: RsvpNote | null = null) => {
    const current = confirmed.current;
    if (!current) return;
    const from = current.mine?.answer ?? null;
    const optimistic = {
      ...shiftCounts(current, from, next),
      mine: next ? { answer: next, note, set_by_coach: false } : null,
    };
    await send(optimistic, { answer: next, note });
  }, [send]);

  const answerFor = useCallback(async (userId: number, next: RsvpAnswer | null) => {
    const current = confirmed.current;
    if (!current?.people) return;
    const person = current.people.find((p) => p.user_id === userId);
    if (!person) return;
    const optimistic = {
      ...shiftCounts(current, person.answer, next),
      people: current.people.map((p) => (p.user_id === userId
        ? { ...p, answer: next, note: null, set_by_coach: next != null }
        : p)),
    };
    await send(optimistic, { answer: next, user_id: userId });
  }, [send]);

  return { rsvp, loading, error, answer, answerFor, reload };
}
