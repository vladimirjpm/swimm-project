/**
 * Хелпер для работы с результатами (группировка, сортировка)
 */
import { Result, TrainingGroup } from '../interfaces/results';
import HelperTime from './helper-time';
import { ageInSeason } from './season-helper';
import { recordAgeAxisNow } from './record-age-axis';

/** Минимум для оси возраста: дата заплыва, год рождения, возраст события из протокола. */
export type RecordStepSource = {
  date?: string;
  birth_year?: number | null;
  event_style_age: string | number;
};

export default class HelperResults {
  /**
   * Заплывы, которые НЕ дают официального места и по умолчанию скрыты:
   * 'prelim' — предварительные, 'extra' — призовые серии после финала (skins, переплывы).
   *
   * Зеркало серверного HeatTypes.GivesOfficialPlace — держим одно правило на клиенте,
   * чтобы бейджи и фильтры не разъезжались со страницей соревнования.
   */
  static isHiddenHeat(heatType?: string | null): boolean {
    return heatType === 'prelim' || heatType === 'extra';
  }

  /**
   * ЕДИНСТВЕННОЕ место, где живёт правило «за это место дают медаль».
   *
   * До 08.09.2026 оно было написано трижды и трижды по-разному: таблица результатов знала
   * про наградность соревнования и предварительные заплывы, страница пловца — только про
   * `prelim`, My media — про снятых и «כללי». Строка при этом рисует медаль ИЗ МЕСТА, и
   * каждое расхождение выходило наружу наградой, которой не вручали.
   *
   * ⚠ Само МЕСТО этим правилом не трогается: его показываем как напечатано в протоколе,
   * включая предварительные (решение Влада 08.09.2026) — пловцу важно видеть, что утром он
   * был первым. Предварительное помечается `UI_PrelimLabel`.
   *
   * Правила — docs/competition-overview-cards.md, «Что считается медалью»: Р34 (prelim и
   * extra — ранжир сессии), Р43 («כללי» = `final-open`: секция без возрастной категории, ни
   * очков, ни медалей), И-18 (снятым место не пишем вовсе), плюс наградность самого
   * соревнования — на лиге мест 1–3 сколько угодно, а наград нет.
   */
  static isMedalPlace(row: {
    place?: number | string | null;
    heatType?: string | null;
    round?: string | null;
    timeFail?: boolean | null;
    competitionIsAward?: boolean | null;
  }): boolean {
    if (!row.competitionIsAward || row.timeFail) return false;
    if (HelperResults.isHiddenHeat(row.heatType)) return false;
    if (row.round === 'final-open') return false;
    const place = Number(row.place);
    return place >= 1 && place <= 3;
  }

  /**
   * Возраст пловца в строке результата — по правилу сезона (год ОКОНЧАНИЯ сезона минус год
   * рождения), а не как посчитала федерация в протоколе.
   *
   * ⚠ Почему не `event_style_age`: протокол считает возраст КАЛЕНДАРНО, и осенние старты
   * уезжают на год младше. מיה גרינברג, 2015 г.р., старт 31/10/2025: в протоколе «10», по
   * правилу сезона 2025/26 — 11 (docs/season-boundary-rule.md, решение Влада 2026-08-22).
   *
   * Fallback на протокольное значение — когда года рождения нет (эстафеты, старые данные).
   */
  /**
   * Возраст-ключ для поиска строки в справочнике рекордов — по оси RecordAgeAxis.
   *
   * ⚠ Это НЕ то же, что `ageLabel`: там возраст ПЛОВЦА (всегда сезонный), здесь — адрес
   * строки в ЧУЖОЙ таблице, и при оси 'calendar' он считается по году заплыва. Осенью
   * числа расходятся на единицу, и это законно (docs/data-integrity.md §13).
   */
  /**
   * Форма, а не весь `Result`: те же три поля есть у строки My media (`MySwimDto`), и
   * ступень рекорда обязана считаться одним кодом на обоих экранах.
   */
  static recordStepAge(res: RecordStepSource): string | number {
    if (recordAgeAxisNow() === 'season') return HelperResults.ageLabel(res);

    const date = parseCompetitionDate(res.date);
    if (!date || !res.birth_year) return res.event_style_age;

    const age = date.getFullYear() - res.birth_year;
    return age > 0 ? age : res.event_style_age;
  }

  static ageLabel(res: RecordStepSource): string | number {
    const date = parseCompetitionDate(res.date);
    const age = res.birth_year ? ageInSeason(res.birth_year, date ?? undefined) : null;
    return age ?? res.event_style_age;
  }

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

/** «dd/MM/yyyy» из протокола → Date. null — формат неизвестен, возраст считаем от «сегодня». */
function parseCompetitionDate(raw?: string | null): Date | null {
  const m = /^(\d{2})\/(\d{2})\/(\d{4})$/.exec((raw || '').trim());
  if (!m) return null;

  const d = new Date(Number(m[3]), Number(m[2]) - 1, Number(m[1]));
  return Number.isNaN(d.getTime()) ? null : d;
}
