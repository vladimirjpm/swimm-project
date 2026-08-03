/**
 * RecordsHelper — рекорды и нормативы с сервера (/api/records, /api/normative-standards),
 * пересобранные в деревья прежней (легаси) формы для синхронных потребителей
 * (helper-normative.ts, popup-content-normative.tsx, normative-age-records.tsx,
 * normative-masters-records.tsx).
 *
 * Национальные рекорды — ключ NR (national record), чей регион — HOME_REGION
 * (utils/constants/home-region.ts); «WR vs ISR» больше нигде не зашито.
 *
 * Геттеры — синхронные: пока сеть не ответила (или упала), отдают пустое дерево
 * («нет данных»), после первого успешного warmUp() — данные из БД. warmUp() дергается
 * один раз при старте приложения (fire-and-forget) — вызывайте геттеры внутри
 * рендера/логики, а не на верхнем уровне модуля, иначе они замёрзнут на состоянии
 * кэша на момент импорта.
 */

import { HOME_REGION, NORMATIVE_COUNTRY } from '../constants/home-region';

type Gender = 'male' | 'female';
type PoolKey = '25m_pool' | '50m_pool';

/** WR — мировой рекорд; NR — национальный рекорд домашнего региона (HOME_REGION). */
export type RecordKind = 'WR' | 'NR';

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
  /** Открытая претензия к записи справочника; null — не оспаривается. */
  issue_reason?: string | null;
  holder_name?: string | null;
  club?: string | null;
  holder_country?: string | null;
  record_date?: string | null;
  updated_at?: string;
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
  issue_reason?: string | null;
  name: string | null;
  country?: string | null;
  record_date?: string | null;
  updated_at?: string;
}
export type OpenRecordsTree = {
  normatives: Record<Gender, Record<PoolKey, Record<string, Record<string, { NR?: OpenRecordCell; WR?: OpenRecordCell }>>>>;
};

export interface AgeRecordCell {
  time: string;
  issue_reason?: string | null;
  name: string | null;
  club?: string | null;
  country?: string | null;
  record_date?: string | null;
  updated_at?: string;
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

/**
 * MAX(updated_at) среди переданных ISO-дат, отформатированный как dd/MM/yyyy для муляжной
 * подписи «updated …» под карточками рекордов. null, если ни одной валидной даты нет
 * (например, RecordsHelper ещё не догрузился и работает legacy-fallback без updated_at).
 */
export function maxUpdatedAtLabel(values: Array<string | undefined | null>): string | null {
  let max: Date | null = null;
  for (const v of values) {
    if (!v) continue;
    const d = new Date(v);
    if (Number.isNaN(d.getTime())) continue;
    if (!max || d > max) max = d;
  }
  if (!max) return null;
  const dd = String(max.getDate()).padStart(2, '0');
  const mm = String(max.getMonth() + 1).padStart(2, '0');
  const yyyy = max.getFullYear();
  return `${dd}/${mm}/${yyyy}`;
}

async function fetchRecords(query: string): Promise<RecordDto[]> {
  const response = await fetch(`/api/records?${query}`);
  if (!response.ok) throw new Error(`GET /api/records?${query} failed: ${response.status}`);
  return (await response.json()) as RecordDto[];
}

async function fetchStandards(kind: string): Promise<NormativeStandardDto[]> {
  const response = await fetch(`/api/normative-standards?kind=${kind}&country=${NORMATIVE_COUNTRY}`);
  if (!response.ok) throw new Error(`GET /api/normative-standards?kind=${kind}&country=${NORMATIVE_COUNTRY} failed: ${response.status}`);
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
      const [world, national] = await Promise.all([
        fetchRecords('region=world'),
        fetchRecords(`region=${HOME_REGION}&category=open`),
      ]);
      const tree: OpenRecordsTree = { normatives: {} as OpenRecordsTree['normatives'] };
      const put = (rec: RecordDto, kind: RecordKind) => {
        if (rec.category !== 'open') return;
        const g = rec.gender as Gender;
        const p = poolKey(rec.pool_type);
        tree.normatives[g] ??= {} as (typeof tree.normatives)[Gender];
        tree.normatives[g][p] ??= {};
        tree.normatives[g][p][rec.style] ??= {};
        tree.normatives[g][p][rec.style][rec.distance] ??= {};
        tree.normatives[g][p][rec.style][rec.distance][kind] = {
          time: rec.time,
          issue_reason: rec.issue_reason ?? null,
          name: rec.holder_name ?? null,
          country: rec.holder_country,
          record_date: rec.record_date,
          updated_at: rec.updated_at,
        };
      };
      world.forEach((r) => put(r, 'WR'));
      national.forEach((r) => put(r, 'NR'));
      this.openCache = tree;
    } catch (error) {
      console.error('RecordsHelper: failed to load open records', error);
    }
  }

  private static async loadCategoryAsAgeTree(category: string): Promise<AgeRecordsTree | null> {
    try {
      const rows = await fetchRecords(`region=${HOME_REGION}&category=${category}`);
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
          issue_reason: rec.issue_reason ?? null,
          name: rec.holder_name ?? null,
          club: rec.club,
          country: rec.holder_country,
          record_date: rec.record_date,
          updated_at: rec.updated_at,
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

  // Fallback до ответа API (или при его недоступности): пустое дерево вместо
  // undefined — потребители видят «нет данных для этой позиции», а не падение на
  // обращении к .normatives. Цепочка window.normative_* удалена: статики normative*.js
  // снесены на этапе 2.7, эти глобалы больше никто не присваивает (проверено grep-ом),
  // а их легаси-форма (ключ ISR вместо NR) всё равно несовместима с текущей.
  private static readonly emptyTree = { normatives: {} };

  /** NR (HOME_REGION) + WR открытые рекорды. */
  static getOpenRecords(): OpenRecordsTree {
    return this.openCache ?? (this.emptyTree as OpenRecordsTree);
  }

  /** Возрастные рекорды домашнего региона (HOME_REGION). */
  static getAgeRecords(): AgeRecordsTree {
    return this.ageCache ?? (this.emptyTree as AgeRecordsTree);
  }

  /** Мастерс-рекорды домашнего региона (HOME_REGION). */
  static getMastersRecords(): AgeRecordsTree {
    return this.mastersCache ?? (this.emptyTree as AgeRecordsTree);
  }

  /** Обычные нормативы уровней. */
  static getStandards(): StandardsTree {
    return this.standardsCache ?? (this.emptyTree as StandardsTree);
  }

  /** Мастерс-нормативы уровней. */
  static getMastersStandards(): MastersStandardsTree {
    return this.mastersStandardsCache ?? (this.emptyTree as MastersStandardsTree);
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
