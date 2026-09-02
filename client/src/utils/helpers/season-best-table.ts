/**
 * Сезонная таблица «лучших в стране» — эталон для бейджа SB в строке протокола.
 *
 * Устроена как справочник рекордов (`records-helper.ts`), и намеренно: строка таблицы
 * результатов должна проверять себя ЛОКАЛЬНО, без запроса на каждую дисциплину. Поэтому
 * сервер отдаёт всю таблицу сезона одним ответом (`GET /api/season-best/table`, ~1,1 тыс.
 * ступеней), она кладётся в память и дальше работает чистой функцией.
 *
 * Ключ ступени — «пол × возраст в сезоне × стиль × дистанция × бассейн», ровно тот, по
 * которому лидера считает сервер. Три вещи, в которых легко ошибиться:
 *
 *  • **ось возраста здесь СЕЗОННАЯ** (`SeasonMath.AgeInSeason` / `ageInSeason` на клиенте),
 *    а не календарная ось справочника рекордов (`HelperResults.recordStepAge`). Это разные
 *    числа, и подставить не то — значит показать бейдж не тому;
 *  • **25м и 50м — разные ступени**: одно время в них несравнимо;
 *  • **порог сверстников**: «первый среди одного» не достижение, и порог тот же, что на
 *    странице пловца (`MinPeersForSeasonBest` на сервере) — иначе одно и то же время было бы
 *    SB в протоколе и не SB в карточке пловца.
 *
 * Состав выборки задан сервером и повторять его здесь нечем: masters-старты, открытая вода,
 * эстафеты и помеченные `SuspectReason` заплывы в таблицу не входят. Вызывающий обязан
 * пропускать такие строки сам (см. `results-table.tsx`).
 */

/** Минимум РАЗНЫХ пловцов на ступени, ниже которого бейдж не выдаётся. */
export const MIN_PEERS_FOR_SEASON_BEST = 2;

export interface SeasonBestTableItem {
  style: string;
  distance: string;
  pool_type: string;
  gender: string;
  age: number;
  time_ms: number;
  peers: number;
}

export interface SeasonBestTableDto {
  season: number;
  season_label: string;
  data: SeasonBestTableItem[];
}

/** Готовая ступень: время лидера и число сверстников. */
export interface SeasonBestStep {
  timeMs: number;
  peers: number;
}

export interface SeasonBestKey {
  styleName: string;
  /** Дистанция в любом виде: «100», «100m», 100 — ключ нормализуется. */
  distance: string | number;
  /** Бассейн в любом виде: «25», «25m». */
  poolType: string | number | null | undefined;
  gender: string;
  /** Возраст В СЕЗОНЕ, не календарный. */
  age: number | null | undefined;
}

const normPool = (pool: unknown): string =>
  String(pool ?? '').trim().toLowerCase().includes('25') ? '25m' : '50m';

const normDistance = (distance: unknown): string =>
  String(distance ?? '').trim().toLowerCase().replace(/m$/, '');

const keyOf = (k: SeasonBestKey): string =>
  `${k.styleName}|${normDistance(k.distance)}|${normPool(k.poolType)}|${k.gender}|${k.age}`;

/**
 * Разбор строки времени в миллисекунды. Свой, а не `parseTimeToSeconds`: сравнение с
 * эталоном идёт на равенство, а секунды с плавающей точкой этого не переживают
 * (`67.44 * 1000 = 67440.00000000001`).
 */
export function timeToMs(time: string | null | undefined): number | null {
  const raw = (time ?? '').trim().replace(',', '.');
  if (!raw) return null;
  const m = raw.match(/^(?:(\d+):)?(\d+)(?:\.(\d{1,3}))?$/);
  if (!m) return null;
  const [, min, sec, frac] = m;
  const hundredths = (frac ?? '').padEnd(3, '0');
  return (Number(min ?? 0) * 60 + Number(sec)) * 1000 + Number(hundredths);
}

export default class SeasonBestTable {
  /** season → готовая мапа ступеней. Заполняется один раз на сезон за жизнь страницы. */
  private static cache = new Map<number, Map<string, SeasonBestStep>>();

  /** season → идущий запрос: два компонента на странице не должны грузить одно дважды. */
  private static inFlight = new Map<number, Promise<void>>();

  /** Грузит таблицу сезона. Повторные вызовы бесплатны. */
  static async load(season: number): Promise<void> {
    if (this.cache.has(season)) return;
    const running = this.inFlight.get(season);
    if (running) return running;

    const request = (async () => {
      try {
        const response = await fetch(`/api/season-best/table?season=${season}`);
        if (!response.ok) throw new Error(`GET /api/season-best/table failed: ${response.status}`);
        const dto = (await response.json()) as SeasonBestTableDto;
        const map = new Map<string, SeasonBestStep>();
        for (const item of dto.data ?? []) {
          map.set(
            keyOf({
              styleName: item.style,
              distance: item.distance,
              poolType: item.pool_type,
              gender: item.gender,
              age: item.age,
            }),
            { timeMs: item.time_ms, peers: item.peers },
          );
        }
        this.cache.set(season, map);
      } catch {
        // Витрина, а не форма: не загрузилось — просто нет бейджей. Пустая мапа в кэше
        // нужна, чтобы страница не долбилась в упавший эндпоинт на каждый ререндер.
        this.cache.set(season, new Map());
      } finally {
        this.inFlight.delete(season);
      }
    })();

    this.inFlight.set(season, request);
    return request;
  }

  /** Загружена ли таблица сезона (для перерисовки строк после прогрева). */
  static isLoaded(season: number): boolean {
    return this.cache.has(season);
  }

  /** Ступень или null — таблицы нет либо в ней такой связки не было ни одного заплыва. */
  static step(season: number, key: SeasonBestKey): SeasonBestStep | null {
    if (key.age == null) return null;
    return this.cache.get(season)?.get(keyOf(key)) ?? null;
  }

  /**
   * Это время — лучшее в сезоне на своей ступени?
   *
   * Равенство, а не «меньше либо равно»: таблица уже содержит минимум сезона, и время
   * быстрее него означало бы, что эталон устарел (кэш сервера живёт сутки) — тогда честнее
   * не показать бейдж, чем показать его строке, которая эталоном ещё не стала.
   * Равных времён бывает несколько — бейдж получают все, как и место в рейтинге.
   */
  static isSeasonBest(season: number, key: SeasonBestKey, timeMs: number | null): boolean {
    if (timeMs == null) return false;
    const step = this.step(season, key);
    if (!step || step.peers < MIN_PEERS_FOR_SEASON_BEST) return false;
    return step.timeMs === timeMs;
  }
}
