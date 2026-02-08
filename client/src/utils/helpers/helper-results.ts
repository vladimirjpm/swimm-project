/**
 * Хелпер для работы с результатами (группировка, сортировка)
 */
import { Result, TrainingGroup } from '../interfaces/results';
import HelperTime from './helper-time';

export default class HelperResults {
  /**
   * Универсальная сортировка по времени
   */
  static sortByTime(arr: Result[]): Result[] {
    return [...arr].sort(
      (a, b) => HelperTime.parseTimeToSeconds(a.time) - HelperTime.parseTimeToSeconds(b.time)
    );
  }

  /**
   * Плоская таблица: одна группа со всеми элементами
   */
  static showTrainingTable(results: Result[]): TrainingGroup[] {
    const date = results[0]?.date ?? '';
    return [
      {
        title: 'All results',
        date,
        items: results.slice(),
      },
    ];
  }

  /**
   * Группировка по имени + дате, сортировка по set/order
   */
  static groupTrainingByName(results: Result[]): TrainingGroup[] {
    const nameOf = (r: Result) =>
      `${r.first_name ?? ''}${r.last_name ? ' ' + r.last_name : ''}`.trim() || '—';
    const dateOf = (r: Result) => r.date ?? '';

    const groups = new Map<string, Result[]>();

    for (const r of results) {
      const key = `${nameOf(r)}||${dateOf(r)}`;
      if (!groups.has(key)) groups.set(key, []);
      groups.get(key)!.push(r);
    }

    const bySetOrder = (a: Result, b: Result) => {
      const sa = Number(a?.training?.set ?? 0);
      const sb = Number(b?.training?.set ?? 0);
      if (sa !== sb) return sa - sb;
      const oa = Number(a?.training?.order ?? 0);
      const ob = Number(b?.training?.order ?? 0);
      return oa - ob;
    };

    const arr: TrainingGroup[] = Array.from(groups.entries()).map(
      ([key, items]) => {
        const [name, date] = key.split('||');
        return {
          title: name,
          name,
          date,
          items: items.slice().sort(bySetOrder),
        };
      },
    );

    arr.sort((g1, g2) => {
      const byName = (g1.name ?? g1.title).localeCompare(g2.name ?? g2.title, 'he');
      if (byName !== 0) return byName;
      return (g1.date ?? '').localeCompare(g2.date ?? '');
    });

    return arr;
  }

  /**
   * Группировка по set + дате, сортировка: order → time → name
   */
  static groupTrainingBySet(results: Result[]): TrainingGroup[] {
    const keyOf = (r: Result) => `${r?.training?.set ?? 0}||${r.date ?? ''}`;
    const map = new Map<string, Result[]>();

    for (const r of results) {
      const k = keyOf(r);
      if (!map.has(k)) map.set(k, []);
      map.get(k)!.push(r);
    }

    const toSec = (t?: string | null) => {
      const s = HelperTime.parseTimeToSeconds(t ?? '');
      return Number.isFinite(s) ? s : Number.POSITIVE_INFINITY;
    };

    const byOrderTimeName = (a: Result, b: Result) => {
      const oa = Number(a?.training?.order ?? 0);
      const ob = Number(b?.training?.order ?? 0);
      if (oa !== ob) return oa - ob;

      const dTime = toSec(a.time) - toSec(b.time);
      if (dTime !== 0) return dTime;

      const an = `${a.first_name ?? ''} ${a.last_name ?? ''}`.trim();
      const bn = `${b.first_name ?? ''} ${b.last_name ?? ''}`.trim();
      return an.localeCompare(bn, 'he');
    };

    const arr: TrainingGroup[] = Array.from(map.entries()).map(
      ([key, items]) => {
        const [setStr, date] = key.split('||');
        const set = Number(setStr);
        return {
          title: `Set ${set}`,
          set,
          date,
          items: items.slice().sort(byOrderTimeName),
        };
      },
    );

    arr.sort((g1, g2) => {
      const s1 = g1.set ?? Number(g1.title.replace(/\D+/g, '') || 0);
      const s2 = g2.set ?? Number(g2.title.replace(/\D+/g, '') || 0);
      if (s1 !== s2) return s1 - s2;
      return (g1.date ?? '').localeCompare(g2.date ?? '');
    });

    return arr;
  }

  /**
   * Фильтрация данных по имени
   */
  static getDolphinDataByName(data: Result[], name: string): Result[] {
    return data.filter((item) => item.first_name === name);
  }

  /**
   * Группировка данных по дате с сортировкой по времени
   */
  static getDolphinDataGroupedByDate(data: Result[]): Record<string, Result[]> {
    const grouped: Record<string, Result[]> = data.reduce(
      (acc, item) => {
        if (!acc[item.date]) {
          acc[item.date] = [];
        }
        acc[item.date].push(item);
        return acc;
      },
      {} as Record<string, Result[]>,
    );

    Object.keys(grouped).forEach((date) => {
      grouped[date] = this.sortByTime(grouped[date]);
    });

    return grouped;
  }
}
