import { useCallback, useEffect, useState } from 'react';
import { fetchAntiforgeryToken, invalidateTokenCache } from '../../../../utils/antiforgery';
import { useAuth } from '../../../../hooks/useAuth';

/**
 * Персональный план на соревнование (docs/plans/start-list-ticket-plan.md, шаг Т3):
 * за кем следит пользователь в табе Start list.
 *
 * ЕДИНСТВЕННЫЙ шов между двумя хранилищами: у гостя план лежит в localStorage, у
 * залогиненного — в профиле (`/api/me/start-list-plans/{orgCompId}`). Компоненты про разницу
 * не знают: ссылку на карточку шлют в родительский чат, и она обязана работать без логина,
 * а у залогиненного тот же план должен открыться с телефона и с ноутбука.
 *
 * ⚠ «Плана нет» (`plan === null`) и «сохранён пустой план» — РАЗНЫЕ состояния. В первом
 * пикер подставляет избранных (пловцов и клубы), во втором человек снял всё сам, и
 * возвращать ему избранных нельзя. Поэтому пустота хранится явно, а не как отсутствие.
 */

export interface StartListPlan {
  swimmer_ids: number[];
  club_ids: number[];
  im_coming: boolean;
  notify_me: boolean;
}

export const EMPTY_PLAN: StartListPlan = {
  swimmer_ids: [],
  club_ids: [],
  im_coming: false,
  notify_me: false,
};

const storageKey = (orgCompId: number) => `swimm.startlist.plan.${orgCompId}`;

/** Что бы ни лежало в хранилище, наружу отдаём план правильной формы. */
function normalize(raw: unknown): StartListPlan | null {
  if (!raw || typeof raw !== 'object') return null;
  const p = raw as Partial<StartListPlan>;
  const ids = (v: unknown): number[] =>
    Array.isArray(v) ? v.filter((n): n is number => typeof n === 'number' && n > 0) : [];
  return {
    swimmer_ids: ids(p.swimmer_ids),
    club_ids: ids(p.club_ids),
    im_coming: p.im_coming === true,
    notify_me: p.notify_me === true,
  };
}

function readLocal(orgCompId: number): StartListPlan | null {
  try {
    const raw = localStorage.getItem(storageKey(orgCompId));
    return raw ? normalize(JSON.parse(raw)) : null;
  } catch {
    // Приватный режим/забитое хранилище — ведём себя как гость без плана.
    return null;
  }
}

function writeLocal(orgCompId: number, plan: StartListPlan | null) {
  try {
    if (plan) localStorage.setItem(storageKey(orgCompId), JSON.stringify(plan));
    else localStorage.removeItem(storageKey(orgCompId));
  } catch {
    /* не сохранилось — план проживёт до перезагрузки, это лучше падения */
  }
}

export interface UseStartListPlan {
  /** null — плана нет; тогда пикер подставляет избранных (см. §Т5). */
  plan: StartListPlan | null;
  loading: boolean;
  /** true — план живёт в профиле, а не в этом браузере. */
  persisted: boolean;
  /** Заменить состав целиком (пикер — экран множественного выбора). */
  save: (next: StartListPlan) => Promise<void>;
  toggleSwimmer: (swimmerId: number) => Promise<void>;
  toggleClub: (clubId: number) => Promise<void>;
  setImComing: (value: boolean) => Promise<void>;
  setNotifyMe: (value: boolean) => Promise<void>;
  /** Забыть план — вернуться к дефолту «мои избранные». */
  clear: () => Promise<void>;
}

export function useStartListPlan(orgCompId: number | null): UseStartListPlan {
  const { isAuthenticated, loading: authLoading } = useAuth();
  const [plan, setPlan] = useState<StartListPlan | null>(null);
  const [loading, setLoading] = useState(true);

  // Пока auth не ответил, хранилище не выбираем: иначе залогиненному на миг покажут
  // гостевой план из localStorage, а потом подменят серверным.
  useEffect(() => {
    if (orgCompId == null || authLoading) return;
    let cancelled = false;
    setLoading(true);

    (async () => {
      if (!isAuthenticated) {
        if (!cancelled) { setPlan(readLocal(orgCompId)); setLoading(false); }
        return;
      }
      try {
        const r = await fetch(`/api/me/start-list-plans/${orgCompId}`, { credentials: 'include' });
        if (cancelled) return;
        // 404 — плана нет; это осмысленный ответ, а не ошибка.
        setPlan(r.status === 404 ? null : r.ok ? normalize(await r.json()) : null);
      } catch {
        if (!cancelled) setPlan(null);
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();

    return () => { cancelled = true; };
  }, [orgCompId, isAuthenticated, authLoading]);

  const persist = useCallback(async (next: StartListPlan | null) => {
    if (orgCompId == null) return;
    setPlan(next);

    if (!isAuthenticated) { writeLocal(orgCompId, next); return; }

    const url = `/api/me/start-list-plans/${orgCompId}`;
    const token = await fetchAntiforgeryToken();
    try {
      const r = await fetch(url, {
        method: next ? 'PUT' : 'DELETE',
        credentials: 'include',
        headers: {
          'Content-Type': 'application/json',
          ...(token ? { 'X-XSRF-TOKEN': token } : {}),
        },
        body: next ? JSON.stringify(next) : undefined,
      });
      // Токен протух вместе с сессией — сбрасываем кэш, чтобы следующая правка взяла свежий.
      if (r.status === 400 || r.status === 403) invalidateTokenCache();
    } catch {
      /* не сохранилось на сервер — на экране состав уже правильный, повторит следующая правка */
    }
  }, [orgCompId, isAuthenticated]);

  const current = plan ?? EMPTY_PLAN;

  const toggleIn = useCallback((list: number[], id: number) =>
    list.includes(id) ? list.filter((x) => x !== id) : [...list, id], []);

  return {
    plan,
    loading: loading || authLoading,
    persisted: isAuthenticated,
    save: (next) => persist(next),
    toggleSwimmer: (id) => persist({ ...current, swimmer_ids: toggleIn(current.swimmer_ids, id) }),
    toggleClub: (id) => persist({ ...current, club_ids: toggleIn(current.club_ids, id) }),
    setImComing: (value) => persist({ ...current, im_coming: value }),
    setNotifyMe: (value) => persist({ ...current, notify_me: value }),
    clear: () => persist(null),
  };
}
