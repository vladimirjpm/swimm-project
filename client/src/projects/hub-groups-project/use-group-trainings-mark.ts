import { useCallback, useEffect, useState } from 'react';
import { lanePlansApi } from './components/lane-plans-api';
import {
  accentMark, goldMark, redMark, tabMark, type TrainingMark, type TrainingMarks,
} from './components/trainings/use-training-marks';
import type { LanePlan, TrainingRsvp, TrainingSessionList } from './types';

/**
 * Метка на самом табе группы «Trainings» — снаружи таба (`marks-spec.md`, «Group tab badge without
 * opening the tab»). Одна, самая срочная: красная › золотая › акцентная (`tabMark`).
 *
 * Откуда что:
 * - красная — из ответов на ближайшее занятие (`rsvp`), их страница и так грузит для шапки;
 * - золотая и акцентная — план дорожек на дату ближайшего занятия и список тренировок. Их грузим
 *   ЛЕНИВО, когда браузер освободится после первой отрисовки: метка не стоит задержки страницы.
 *   Общей ручки `/me/marks` нет сознательно (хендофф) — завести, только если это станет дорого.
 *
 * Пока таб открыт, золотую и акцентную подаёт сам таб (`reportTabMarks` ← `onMarksChange`): там
 * гасится «увидел» и видны правки плана. Последнее поданное держим и после ухода с таба — наша
 * ленивая загрузка к тому времени устарела бы (сервер уже отметил «увидел»).
 */
export function useGroupTrainingsMark({
  groupId, nextDate, manages, rsvp, enabled,
}: {
  groupId: number | null;
  /** Дата ближайшего занятия (`group.next_training.date`); нет занятия — плана не ищем. */
  nextDate: string | null;
  manages: boolean;
  rsvp: TrainingRsvp | null;
  /** Только своим (участнику / управляющему): остальным сервер ответил бы 403. */
  enabled: boolean;
}): { mark: TrainingMark | null; reportTabMarks: (marks: TrainingMarks) => void } {
  const [plan, setPlan] = useState<LanePlan | null>(null);
  const [sessions, setSessions] = useState<TrainingSessionList | null>(null);
  const [fromTab, setFromTab] = useState<Pick<TrainingMarks, 'lanes' | 'trainings'> | null>(null);

  useEffect(() => {
    setPlan(null);
    setSessions(null);
    setFromTab(null);
    if (!enabled || groupId == null) return undefined;
    let alive = true;
    const load = () => {
      fetch(`/api/hub-groups/${groupId}/sessions`, { credentials: 'include', cache: 'no-store' })
        .then((r) => (r.ok ? (r.json() as Promise<TrainingSessionList>) : null))
        .then((data) => { if (alive) setSessions(data); })
        .catch(() => { /* метка — необязательная подсказка: нет ответа — нет метки */ });
      if (nextDate) {
        lanePlansApi.get(groupId, nextDate).then((res) => {
          if (alive && res.ok && res.data) setPlan(res.data);
        });
      }
    };
    // После первой отрисовки, когда браузер свободен; Safari без requestIdleCallback — таймером.
    const idle = typeof window.requestIdleCallback === 'function'
      ? window.requestIdleCallback(load, { timeout: 2000 })
      : null;
    const timer = idle == null ? window.setTimeout(load, 300) : null;
    return () => {
      alive = false;
      if (idle != null) window.cancelIdleCallback(idle);
      if (timer != null) window.clearTimeout(timer);
    };
  }, [enabled, groupId, nextDate]);

  const reportTabMarks = useCallback((marks: TrainingMarks) => {
    setFromTab((prev) => (prev?.lanes === marks.lanes && prev?.trainings === marks.trainings
      ? prev
      : { lanes: marks.lanes, trainings: marks.trainings }));
  }, []);

  if (!enabled) return { mark: null, reportTabMarks };
  const mark = tabMark({
    next: redMark(rsvp),
    lanes: fromTab ? fromTab.lanes : goldMark(plan, manages),
    trainings: fromTab ? fromTab.trainings : accentMark(sessions),
  });
  return { mark, reportTabMarks };
}
