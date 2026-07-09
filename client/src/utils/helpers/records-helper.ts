/**
 * RecordsHelper — рекорды и нормативы с сервера (/api/records, /api/normative-standards),
 * пересобранные в легаси-форму window.normative_record / window.normative(_masters).
 *
 * Мягкий переход (см. docs/tasks/phase2-records-client-sonnet.md, этап 2.4): пока
 * normative*.js остаются подключены как script-теги, методы отдают идентичную по форме
 * структуру, чтобы существующие потребители (helper-normative.ts, popup-content-normative.tsx,
 * normative-age-records.tsx, normative-masters-records.tsx) менялись минимально.
 *
 * Геттеры — синхронные (потребители синхронные): пока сеть не ответила (или упала),
 * отдают window.normative_* as-is; после первого успешного warmUp() — данные из БД.
 * warmUp() дергается один раз при старте приложения (fire-and-forget) — вызывайте
 * геттеры внутри рендера/логики, а не на верхнем уровне модуля, иначе они замёрзнут
 * на состоянии кэша на момент импорта.
 */

type Gender = 'male' | 'female';
type PoolKey = '25m_pool' | '50m_pool';

interface RecordDto {
  region_type: string;
  region_code: string;
  category: string;
  age_key: string;
  gender: string;
  pool_type: string;
  style: string;
  distance: string;
  time: string;
  holder_name?: string | null;
  club?: string | null;
  holder_country?: string | null;
  record_date?: string | null;
}

interface NormativeStandardDto {
  kind: string;
  country: string;
  gender: string;
  pool_type: string;
  style: string;
  distance: string;
  age_key: string;
  level: string;
  time: string;
}

export interface OpenRecordCell {
  time: string;
  name: string | null;
  country?: string | null;
  record_date?: string | null;
}
export type OpenRecordsTree = {
  normatives: Record<Gender, Record<PoolKey, Record<string, Record<string, { ISR?: OpenRecordCell; WR?: OpenRecordCell }>>>>;
};

export interface AgeRecordCell {
  time: string;
  name: string | null;
  club?: string | null;
  country?: string | null;
  record_date?: string | null;
}
export type AgeRecordsTree = {
  normatives: Record<Gender, Record<PoolKey, Record<string, Record<string, Record<string, AgeRecordCell>>>>>;
};

/** style -> distance -> level -> time. */
export type StandardsTree = {
  normatives: Record<Gender, Record<PoolKey, Record<string, Record<string, Record<string, string>>>>>;
};

/** style -> distance -> ageGroup -> level -> time. */
export type MastersStandardsTree = {
  normatives: Record<Gender, Record<PoolKey, Record<string, Record<string, Record<string, Record<string, string>>>>>>;
};

const poolKey = (poolType: string): PoolKey => (poolType === '50m' ? '50m_pool' : '25m_pool');

async function fetchRecords(query: string): Promise<RecordDto[]> {
  const response = await fetch(`/api/records?${query}`);
  if (!response.ok) throw new Error(`GET /api/records?${query} failed: ${response.status}`);
  return (await response.json()) as RecordDto[];
}

async function fetchStandards(kind: string): Promise<NormativeStandardDto[]> {
  const response = await fetch(`/api/normative-standards?kind=${kind}`);
  if (!response.ok) throw new Error(`GET /api/normative-standards?kind=${kind} failed: ${response.status}`);
  return (await response.json()) as NormativeStandardDto[];
}

export default class RecordsHelper {
  private static openCache: OpenRecordsTree | null = null;
  private static ageCache: AgeRecordsTree | null = null;
  private static mastersCache: AgeRecordsTree | null = null;
  private static standardsCache: StandardsTree | null = null;
  private static mastersStandardsCache: MastersStandardsTree | null = null;
  private static warmedUp = false;

  /** Разовый прогрев всех пяти ресурсов (fire-and-forget). Вызывать один раз при старте приложения. */
  static warmUp(): void {
    if (this.warmedUp) return;
    this.warmedUp = true;
    void this.loadOpenRecords();
    void this.loadAgeRecords();
    void this.loadMastersRecords();
    void this.loadStandards();
    void this.loadMastersStandards();
  }

  private static async loadOpenRecords(): Promise<void> {
    try {
      const [world, isr] = await Promise.all([
        fetchRecords('region=world'),
        fetchRecords('region=ISR&category=open'),
      ]);
      const tree: OpenRecordsTree = { normatives: {} as OpenRecordsTree['normatives'] };
      const put = (rec: RecordDto, kind: 'ISR' | 'WR') => {
        if (rec.category !== 'open') return;
        const g = rec.gender as Gender;
        const p = poolKey(rec.pool_type);
        tree.normatives[g] ??= {} as (typeof tree.normatives)[Gender];
        tree.normatives[g][p] ??= {};
        tree.normatives[g][p][rec.style] ??= {};
        tree.normatives[g][p][rec.style][rec.distance] ??= {};
        tree.normatives[g][p][rec.style][rec.distance][kind] = {
          time: rec.time,
          name: rec.holder_name ?? null,
          country: rec.holder_country,
          record_date: rec.record_date,
        };
      };
      world.forEach((r) => put(r, 'WR'));
      isr.forEach((r) => put(r, 'ISR'));
      this.openCache = tree;
    } catch (error) {
      console.error('RecordsHelper: failed to load open records', error);
    }
  }

  private static async loadCategoryAsAgeTree(category: string): Promise<AgeRecordsTree | null> {
    try {
      const rows = await fetchRecords(`region=ISR&category=${category}`);
      const tree: AgeRecordsTree = { normatives: {} as AgeRecordsTree['normatives'] };
      rows.forEach((rec) => {
        const g = rec.gender as Gender;
        const p = poolKey(rec.pool_type);
        tree.normatives[g] ??= {} as (typeof tree.normatives)[Gender];
        tree.normatives[g][p] ??= {};
        tree.normatives[g][p][rec.style] ??= {};
        tree.normatives[g][p][rec.style][rec.distance] ??= {};
        tree.normatives[g][p][rec.style][rec.distance][rec.age_key] = {
          time: rec.time,
          name: rec.holder_name ?? null,
          club: rec.club,
          country: rec.holder_country,
          record_date: rec.record_date,
        };
      });
      return tree;
    } catch (error) {
      console.error(`RecordsHelper: failed to load records category=${category}`, error);
      return null;
    }
  }

  private static async loadAgeRecords(): Promise<void> {
    const tree = await this.loadCategoryAsAgeTree('age');
    if (tree) this.ageCache = tree;
  }

  private static async loadMastersRecords(): Promise<void> {
    const tree = await this.loadCategoryAsAgeTree('masters');
    if (tree) this.mastersCache = tree;
  }

  private static async loadStandards(): Promise<void> {
    try {
      const rows = await fetchStandards('regular');
      const tree: StandardsTree = { normatives: {} as StandardsTree['normatives'] };
      rows.forEach((s) => {
        const g = s.gender as Gender;
        const p = poolKey(s.pool_type);
        tree.normatives[g] ??= {} as (typeof tree.normatives)[Gender];
        tree.normatives[g][p] ??= {};
        tree.normatives[g][p][s.style] ??= {};
        tree.normatives[g][p][s.style][s.distance] ??= {};
        tree.normatives[g][p][s.style][s.distance][s.level] = s.time;
      });
      this.standardsCache = tree;
    } catch (error) {
      console.error('RecordsHelper: failed to load standards (regular)', error);
    }
  }

  private static async loadMastersStandards(): Promise<void> {
    try {
      const rows = await fetchStandards('masters');
      const tree: MastersStandardsTree = { normatives: {} as MastersStandardsTree['normatives'] };
      rows.forEach((s) => {
        const g = s.gender as Gender;
        const p = poolKey(s.pool_type);
        tree.normatives[g] ??= {} as (typeof tree.normatives)[Gender];
        tree.normatives[g][p] ??= {};
        tree.normatives[g][p][s.style] ??= {};
        tree.normatives[g][p][s.style][s.distance] ??= {};
        tree.normatives[g][p][s.style][s.distance][s.age_key] ??= {};
        tree.normatives[g][p][s.style][s.distance][s.age_key][s.level] = s.time;
      });
      this.mastersStandardsCache = tree;
    } catch (error) {
      console.error('RecordsHelper: failed to load standards (masters)', error);
    }
  }

  // Последний уровень fallback (статики normative*.js удалены на этапе 2.7): пустое
  // дерево вместо undefined — до ответа API (или при его недоступности) потребители
  // видят «нет данных для этой позиции», а не падение на обращении к .normatives.
  // window.normative_* оставлены в цепочке на случай легаси-страниц со script-тегами.
  private static readonly emptyTree = { normatives: {} };

  /** ISR+WR открытые рекорды (форма window.normative_record). */
  static getOpenRecords(): OpenRecordsTree {
    return (
      this.openCache ??
      ((window as any).normative_record as OpenRecordsTree) ??
      (this.emptyTree as OpenRecordsTree)
    );
  }

  /** Возрастные рекорды ISR (форма window.normative_age_record). */
  static getAgeRecords(): AgeRecordsTree {
    return (
      this.ageCache ??
      ((window as any).normative_age_record as AgeRecordsTree) ??
      (this.emptyTree as AgeRecordsTree)
    );
  }

  /** Мастерс-рекорды ISR (форма window.normative_masters_record). */
  static getMastersRecords(): AgeRecordsTree {
    return (
      this.mastersCache ??
      ((window as any).normative_masters_record as AgeRecordsTree) ??
      (this.emptyTree as AgeRecordsTree)
    );
  }

  /** Обычные нормативы уровней (форма window.normative). */
  static getStandards(): StandardsTree {
    return (
      this.standardsCache ??
      ((window as any).normative as StandardsTree) ??
      (this.emptyTree as StandardsTree)
    );
  }

  /** Мастерс-нормативы уровней (форма window.normative_masters / варианты имени в легаси). */
  static getMastersStandards(): MastersStandardsTree {
    return (
      this.mastersStandardsCache ??
      (((window as any).normatives_masters ||
        (window as any).normativesMasters ||
        (window as any).normative_masters ||
        (window as any).normativeMasters ||
        null) as MastersStandardsTree) ??
      (this.emptyTree as MastersStandardsTree)
    );
  }

  /** Сброс кэша (тестирование). */
  static clearCache(): void {
    this.openCache = null;
    this.ageCache = null;
    this.mastersCache = null;
    this.standardsCache = null;
    this.mastersStandardsCache = null;
    this.warmedUp = false;
  }
}
