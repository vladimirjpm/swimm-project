import React, { useEffect, useMemo, useRef, useState } from 'react';
import { useAppSelector } from '../../../store/store';
import Helper from '../../../utils/helpers/data-helper';
import RecordsHelper, { maxUpdatedAtLabel } from '../../../utils/helpers/records-helper';
import { HOME_REGION, HOME_REGION_LABEL } from '../../../utils/constants/home-region';
import useMode from '../../../hooks/useMode';
import './popup-content-normative.css';

// ==== Типы данных ====
type PoolType = '25m_pool' | '50m_pool';
type Gender = 'male' | 'female';
type LevelsMap = Record<string, number>;
type DistancesMap = Record<string, LevelsMap>;
type PoolMapLoose = Record<string, DistancesMap>;
type Normatives = {
  normatives: Record<Gender, Record<PoolType, PoolMapLoose>>;
};

type RecordCell = {
  time: number | null;
  name: string | null;
  country?: string | null;
  record_date?: string | null;
  updated_at?: string;
};
type RecordsTree = {
  normatives: Record<
    Gender,
    Record<
      PoolType,
      Record<
        string, // stroke
        Record<
          string, // distance
          {
            NR?: RecordCell;
            WR?: RecordCell;
          }
        >
      >
    >
  >;
};

// Данные приходят из RecordsHelper (БД, /api/records + /api/normative-standards);
// до загрузки — пустое дерево. NR = национальный рекорд HOME_REGION (см. records-helper.ts).

const strokeLabel: Record<string, string> = {
  freestyle: 'Freestyle',
  butterfly: 'Butterfly',
  breaststroke: 'Breaststroke',
  backstroke: 'Backstroke',
  individual_medley: 'Individual Medley',
};

// ===== Нормализация значений из popUpObj =====
const derivePoolType = (val?: string): PoolType => {
  // If already a canonical pool type like '50m_pool' or '25m_pool', use it
  if (!val) return '25m_pool';
  const v = String(val).toLowerCase();
  if (v.includes('50')) return '50m_pool';
  if (v.includes('25')) return '25m_pool';
  // default fallback
  return '25m_pool';
};

const deriveStroke = (styleName?: string): string => {
  const key = (styleName || '').toLowerCase().trim();
  return Object.keys(strokeLabel).includes(key) ? key : 'freestyle';
};

// 50 -> 50m
const normalizeDistance = (v?: string) => {
  if (!v) return '';
  const s = String(v)
    .trim()
    .toLowerCase()
    .replace(/м/g, 'm')
    .replace(/\s+/g, '');
  const m = s.match(/^(\d+)(m)?$/);
  return m ? `${m[1]}m` : s;
};

// ===== helpers для рекордов =====
const getRecordCell = (
  recordsTree: RecordsTree,
  gender: Gender,
  poolType: PoolType,
  stroke: string,
  distance: string,
  kind: 'NR' | 'WR',
): RecordCell | null => {
  const poolNode =
    recordsTree?.normatives?.[gender]?.[poolType] ||
    // fallback на 50м, если 25м нет
    recordsTree?.normatives?.[gender]?.['50m_pool'];
  const byStroke = poolNode?.[stroke];
  const byDist = byStroke?.[distance];
  return byDist?.[kind] ?? null;
};

// ==================== Дизайн-токены (design_handoff_normative_info) ====================

type Cat = 'pro' | 'adult' | 'youth' | 'rec';
type CatColors = { accent: string; deep: string; soft: string; border: string };

// Палитра категорий уровней (pro / adult / youth) + нейтраль для рекордов
const CAT_LIGHT: Record<Cat, CatColors> = {
  pro:   { accent: '#ea580c', deep: '#7c2d12', soft: '#fff0e6', border: '#fcd5bb' },
  adult: { accent: '#4f46e5', deep: '#312e81', soft: '#eeecfe', border: '#d6d1fb' },
  youth: { accent: '#16a34a', deep: '#14532d', soft: '#e7f6ed', border: '#c2ebd1' },
  rec:   { accent: '#475569', deep: '#0f172a', soft: '#eef1f6', border: '#d7dde6' },
};
const CAT_DARK: Record<Cat, CatColors> = {
  pro:   { accent: '#ff8a4c', deep: '#ffd9c2', soft: 'rgba(255,138,76,.16)', border: '#5a3320' },
  adult: { accent: '#8b83ff', deep: '#dcd8ff', soft: 'rgba(139,131,255,.17)', border: '#332f5c' },
  youth: { accent: '#3ecb7f', deep: '#c7f0d8', soft: 'rgba(62,203,127,.15)', border: '#245038' },
  rec:   { accent: '#94a3b8', deep: '#e2e8f0', soft: 'rgba(148,163,184,.14)', border: '#2b3442' },
};

// Поверхности / текст / пилюли M-W
const UI_LIGHT = {
  card: '#ffffff', cardBorder: '#e6e9ee', cardShadow: '0 20px 60px rgba(20,28,45,.14)',
  subtle: '#f1f3f7', grid: '#eceff4', text: '#141824', label: '#8a93a2',
  menPill: '#e6f0fd', menText: '#1d4ed8', womenPill: '#fdeaf2', womenText: '#be185d',
  rowWash: '#fffbeb', rowWashSoft: '#fffdf5',
};
const UI_DARK: typeof UI_LIGHT = {
  card: '#151a22', cardBorder: '#232b36', cardShadow: '0 20px 60px rgba(0,0,0,.5)',
  subtle: '#1c232e', grid: '#232b36', text: '#eef2f7', label: '#8794a4',
  menPill: 'rgba(90,162,245,.16)', menText: '#9cc4fb', womenPill: 'rgba(240,114,166,.16)', womenText: '#f6a9cd',
  rowWash: 'rgba(245,184,0,.10)', rowWashSoft: 'rgba(245,184,0,.05)',
};

// Лестница уровней (сильный → слабый, слева направо)
const LADDER: { key: string; label: string; cat: Cat }[] = [
  { key: 'MSMK', label: 'MSMK', cat: 'pro' },
  { key: 'MS', label: 'MS', cat: 'pro' },
  { key: 'KMS', label: 'KMS', cat: 'pro' },
  { key: 'I_adult', label: 'I', cat: 'adult' },
  { key: 'II_adult', label: 'II', cat: 'adult' },
  { key: 'III_adult', label: 'III', cat: 'adult' },
  { key: 'I_youth', label: '1y', cat: 'youth' },
  { key: 'II_youth', label: '2y', cat: 'youth' },
  { key: 'III_youth', label: '3y', cat: 'youth' },
];

const STROKES: [string, string][] = [
  ['freestyle', 'Freestyle'],
  ['backstroke', 'Backstroke'],
  ['breaststroke', 'Breaststroke'],
  ['butterfly', 'Butterfly'],
  ['individual_medley', 'Medley'],
];

// Приоритет уровней для сортировки masters-лестницы (от слабого к сильному)
const LEVEL_PRIORITY: Record<string, number> = {
  'III_youth': 1, 'II_youth': 2, 'I_youth': 3,
  'III_adult': 4, 'II_adult': 5, 'I_adult': 6,
  // Masters levels
  '3': 4, '2': 5, '1': 6,
  'KMS': 7, 'MS': 8, 'MSMK': 9,
};

const LEVEL_LABELS: Record<string, string> = {
  MSMK: 'MSMK', MS: 'MS', KMS: 'KMS',
  I_adult: 'I', II_adult: 'II', III_adult: 'III',
  I_youth: '1y', II_youth: '2y', III_youth: '3y',
  '1': 'I', '2': 'II', '3': 'III',
};

const catOfLevel = (key: string): Cat => {
  const k = key.toLowerCase();
  if (k.includes('youth')) return 'youth';
  if (k === 'kms' || k === 'ms' || k === 'msmk') return 'pro';
  return 'adult';
};

// Вариант медальонов: 'A' (Aurora, мягкие чипы) / 'B' (Podium, залитые accent).
// Единственное отличие вариантов — заливка медальона (см. README handoff).
const VARIANT = 'A' as 'A' | 'B';

const strokeIconUrl = (stroke: string) =>
  `${import.meta.env.BASE_URL}images/swimm-style-icon/${stroke}.png`;

// Мобильный compare-вид включается ниже ~760px (breakpoint прототипа)
const useIsMobile = () => {
  const [mobile, setMobile] = useState(
    () => typeof window !== 'undefined' && window.matchMedia('(max-width: 759px)').matches,
  );
  useEffect(() => {
    const mq = window.matchMedia('(max-width: 759px)');
    const onChange = (e: MediaQueryListEvent) => setMobile(e.matches);
    mq.addEventListener('change', onChange);
    return () => mq.removeEventListener('change', onChange);
  }, []);
  return mobile;
};

const PopupContentNormative: React.FC = () => {
  const { levelName = '', styleName = '', styleLen = '', poolLen = '', poolType: popUpPoolType, isMasters = false, normativeAgeGroup } =
    useAppSelector((state) => state.popUpObj);

  // Приведение isMasters к булеву типу (строгое сравнение + строка/число)
  const isMastersBool = isMasters === true || isMasters === 'true' || isMasters === 1;

  // Данные рекордов/нормативов — читаем на каждый рендер (RecordsHelper может догрузиться позже)
  const norms = RecordsHelper.getStandards() as unknown as Normatives;
  const normsMasters = RecordsHelper.getMastersStandards() as unknown as Normatives | null;
  const recordsTree = RecordsHelper.getOpenRecords() as unknown as RecordsTree;

  const { mode } = useMode();
  const dark = mode === 'dark';
  const C = dark ? CAT_DARK : CAT_LIGHT;
  const U = dark ? UI_DARK : UI_LIGHT;
  const isMobile = useIsMobile();

  const [poolType, setPoolType] = useState<PoolType>(derivePoolType(popUpPoolType ?? poolLen));
  const [stroke, setStroke] = useState<string>(deriveStroke(styleName));
  // selectedAgeGroup только если masters-режим, иначе пусто
  const [selectedAgeGroup, setSelectedAgeGroup] = useState<string>(isMastersBool ? (normativeAgeGroup ?? '25-29') : '');
  // Дистанция пловца — стартует со styleLen, меняется чипами в дропдауне
  const [dist, setDist] = useState<string>(() => normalizeDistance(styleLen));
  // UI-состояние редизайна
  const [styleOpen, setStyleOpen] = useState(false);
  const [pairIdx, setPairIdx] = useState<number | null>(null); // мобильный пейджер
  const [navDir, setNavDir] = useState<'next' | 'prev' | null>(null);
  const [othersOpen, setOthersOpen] = useState(false);
  const ddRef = useRef<HTMLDivElement>(null);

  // Закрытие дропдауна по клику снаружи
  useEffect(() => {
    if (!styleOpen) return;
    const onDown = (e: MouseEvent) => {
      if (ddRef.current && !ddRef.current.contains(e.target as Node)) setStyleOpen(false);
    };
    document.addEventListener('mousedown', onDown);
    return () => document.removeEventListener('mousedown', onDown);
  }, [styleOpen]);

  // Получаем список доступных возрастных групп из normatives-masters (только если isMastersBool)
  const availableAgeGroups = useMemo(() => {
    if (!isMastersBool || !normsMasters) return [];
    const malePool = normsMasters.normatives?.male?.[poolType];
    if (!malePool) return [];
    const firstStroke = Object.keys(malePool)[0];
    if (!firstStroke) return [];
    const firstDist = Object.keys(malePool[firstStroke])[0];
    if (!firstDist) return [];
    const ageGroups = Object.keys(malePool[firstStroke][firstDist]);
    return ageGroups.sort((a, b) => {
      const numA = parseInt(a.split('-')[0], 10);
      const numB = parseInt(b.split('-')[0], 10);
      return numA - numB;
    });
  }, [isMastersBool, poolType]);

  // Выбираем источник нормативов и структуру только если isMastersBool
  let maleStyle: DistancesMap = {};
  let femaleStyle: DistancesMap = {};
  if (isMastersBool && normsMasters) {
    const malePool = normsMasters.normatives.male[poolType] || {};
    const femalePool = normsMasters.normatives.female[poolType] || {};
    // Для masters структура: stroke -> distance -> ageGroup -> levels
    const resolveStyleData = (poolData: PoolMapLoose): DistancesMap => {
      const styleData = poolData[stroke] ?? {};
      if (!selectedAgeGroup) return styleData;
      const firstDistKey = Object.keys(styleData)[0];
      if (!firstDistKey) return styleData;
      const firstDistData = styleData[firstDistKey];
      if (!firstDistData || typeof firstDistData !== 'object') return styleData;
      const subKeys = Object.keys(firstDistData);
      const looksLikeAgeGroups = subKeys.length > 0 && subKeys.every(k => /^\d{2}-\d{2}$/.test(k));
      if (!looksLikeAgeGroups) return styleData;
      const result: DistancesMap = {};
      for (const d of Object.keys(styleData)) {
        const distData = styleData[d];
        if (distData && typeof distData === 'object' && distData[selectedAgeGroup]) {
          result[d] = distData[selectedAgeGroup] as unknown as LevelsMap;
        }
      }
      return result;
    };
    maleStyle = resolveStyleData(malePool);
    femaleStyle = resolveStyleData(femalePool);
  } else {
    // Обычные нормативы
    const malePool = norms.normatives.male[poolType] || {};
    const femalePool = norms.normatives.female[poolType] || {};
    maleStyle = malePool[stroke] ?? {};
    femaleStyle = femalePool[stroke] ?? {};
  }

  const normToSeconds = (v: unknown): number => {
    if (typeof v === 'number' && isFinite(v)) return v;
    return Helper.parseTimeToSeconds(String(v ?? ''));
  };
  const fmt = (v: unknown) => Helper.formatSecondsToTimeString(normToSeconds(v));

  const allDistanceKeys = Array.from(
    new Set([...Object.keys(maleStyle), ...Object.keys(femaleStyle)]),
  ).sort((a, b) => parseInt(a) - parseInt(b));

  // Актуальная дистанция: выбранная чипом, иначе первая доступная
  const effDist = allDistanceKeys.includes(dist) ? dist : (allDistanceKeys[0] ?? '');

  // Лестница уровней: обычный режим — фиксированная; masters — из данных
  const ladder = useMemo(() => {
    if (!isMastersBool) return LADDER;
    const set = new Set<string>();
    allDistanceKeys.forEach((d) => {
      Object.keys(maleStyle[d] || {}).forEach((k) => set.add(k));
      Object.keys(femaleStyle[d] || {}).forEach((k) => set.add(k));
    });
    return Array.from(set)
      .sort((a, b) => (LEVEL_PRIORITY[b] ?? 0) - (LEVEL_PRIORITY[a] ?? 0)) // сильный → слабый
      .map((k) => ({ key: k, label: LEVEL_LABELS[k] ?? k, cat: catOfLevel(k) }));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [isMastersBool, poolType, stroke, selectedAgeGroup]);

  // Текущий уровень пловца в лестнице (case-insensitive)
  const myIdx = ladder.findIndex((l) => l.key.toLowerCase() === (levelName || '').toLowerCase());

  // ==== Медальон уровня (вариант A: мягкий чип; B: заливка accent) ====
  const medStyle = (cc: CatColors): React.CSSProperties => ({
    display: 'inline-flex', alignItems: 'center', justifyContent: 'center',
    minWidth: 46, height: 36, padding: '0 12px', borderRadius: 11,
    fontWeight: 900, fontSize: 14, letterSpacing: '.01em',
    ...(VARIANT === 'B'
      ? { background: cc.accent, color: '#fff', border: `1.5px solid ${cc.accent}` }
      : { background: cc.soft, color: cc.deep, border: `1.5px solid ${cc.border}` }),
  });

  const menPillStyle: React.CSSProperties = {
    borderRadius: 8, padding: '5px 4px', textAlign: 'center',
    fontSize: 13, fontWeight: 700, fontVariantNumeric: 'tabular-nums',
    background: U.menPill, color: U.menText,
  };
  const womenPillStyle: React.CSSProperties = { ...menPillStyle, background: U.womenPill, color: U.womenText };

  const fmtRecord = (g: Gender, d: string, kind: 'WR' | 'NR') => {
    const cell = getRecordCell(recordsTree, g, poolType, stroke, d, kind);
    return {
      text: Helper.formatSecondsToTimeString(Helper.parseTimeToSeconds(String(cell?.time ?? ''))),
      holder: cell?.name ?? undefined,
      updatedAt: cell?.updated_at,
    };
  };

  // Приглушённая подпись «updated dd/MM/yyyy» над колонками WR/NR — сводно по всем
  // рекордам (оба пола, обе колонки), которые сейчас видны в таблице (все дистанции текущего
  // стиля/бассейна). Только записи Records имеют updated_at — нормативы (лестница уровней)
  // это поле не несут и не участвуют.
  const recordsUpdatedLabel = useMemo(() => {
    const stamps: Array<string | undefined> = [];
    allDistanceKeys.forEach((d) => {
      (['WR', 'NR'] as const).forEach((kind) => {
        (['male', 'female'] as Gender[]).forEach((g) => {
          stamps.push(getRecordCell(recordsTree, g, poolType, stroke, d, kind)?.updated_at);
        });
      });
    });
    return maxUpdatedAtLabel(stamps);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [recordsTree, poolType, stroke, allDistanceKeys.join(',')]);

  // ==== Мобильный пейджер: пара [текущий, следующий (быстрее)] ====
  const rawPairIdx = pairIdx ?? (myIdx > 0 ? myIdx : 1);
  const pIdx = Math.max(1, Math.min(ladder.length - 1, rawPairIdx));
  const leftLevel = ladder[pIdx];        // текущий (фокус)
  const rightLevel = ladder[pIdx - 1];   // следующий, быстрее
  const canNext = pIdx > 1;                    // к более сильным
  const canPrev = pIdx < ladder.length - 1;    // к более слабым
  const goPrev = () => { if (!canPrev) return; setPairIdx(pIdx + 1); setNavDir('prev'); };
  const goNext = () => { if (!canNext) return; setPairIdx(pIdx - 1); setNavDir('next'); };

  // ==================== Рендер ====================

  const adultAccent = C.adult.accent;

  return (
    <div className="max-h-[75vh] overflow-auto">

      {/* ===== Заголовок (✕ рисует контейнер попапа) ===== */}
      <div className="flex items-center gap-2.5 pr-8">
        <div
          className="flex items-center justify-center"
          style={{ width: 34, height: 34, borderRadius: 10, background: U.subtle, fontSize: 18 }}
        >
          📊
        </div>
        <div>
          <div style={{ fontSize: 20, fontWeight: 900, color: U.text, letterSpacing: '-0.5px', lineHeight: 1 }}>
            Normative Info
          </div>
          <div style={{ fontSize: 12, fontWeight: 600, color: U.label, marginTop: 3 }}>
            {strokeLabel[stroke]}
            {isMastersBool && selectedAgeGroup ? ` · ${selectedAgeGroup}` : ''}
          </div>
        </div>
      </div>

      {/* ===== Контролы: бассейн + Swimming Style (стиль · дистанция) ===== */}
      <div className="mt-3.5 flex flex-wrap items-center gap-4">
        <div style={{ display: 'inline-flex', background: U.subtle, borderRadius: 11, padding: 3 }}>
          {(['25m_pool', '50m_pool'] as PoolType[]).map((pt) => {
            const active = poolType === pt;
            return (
              <button
                key={pt}
                onClick={() => setPoolType(pt)}
                aria-pressed={active}
                style={{
                  fontSize: 12, fontWeight: 700, padding: '6px 13px', borderRadius: 8,
                  cursor: 'pointer', whiteSpace: 'nowrap', border: 'none', transition: 'all .18s ease',
                  background: active ? adultAccent : 'transparent',
                  color: active ? '#fff' : U.label,
                }}
              >
                {pt === '25m_pool' ? '25m' : '50m'}
              </button>
            );
          })}
        </div>

        <div ref={ddRef} style={{ position: 'relative' }}>
          <button
            onClick={() => setStyleOpen((o) => !o)}
            aria-expanded={styleOpen}
            style={{
              display: 'inline-flex', alignItems: 'center', gap: 12, cursor: 'pointer',
              padding: '11px 18px', borderRadius: 13, background: U.subtle, border: 'none', userSelect: 'none',
            }}
          >
            <span style={{ fontSize: 16, fontWeight: 800, color: U.text }}>Swimming Style</span>
            <span
              style={{
                display: 'inline-block', width: 72, height: 36,
                backgroundImage: `url("${strokeIconUrl(stroke)}")`,
                backgroundRepeat: 'no-repeat', backgroundPosition: 'center', backgroundSize: 'contain',
              }}
            />
            <span style={{ fontSize: 16, fontWeight: 800, color: adultAccent }}>· {effDist}</span>
            <span
              style={{
                fontSize: 12, color: adultAccent,
                transform: styleOpen ? 'rotate(0deg)' : 'rotate(180deg)', transition: 'transform .2s ease',
              }}
            >
              ▲
            </span>
          </button>

          {styleOpen && (
            <div
              style={{
                position: 'absolute', top: 'calc(100% + 8px)', left: 0, zIndex: 30,
                background: U.card, border: `1px solid ${U.cardBorder}`, borderRadius: 16,
                boxShadow: U.cardShadow, padding: 14, display: 'flex', gap: 14, alignItems: 'flex-start',
              }}
            >
              {/* Стили — колонка слева */}
              <div style={{ display: 'flex', flexDirection: 'column', gap: 8, paddingRight: 14, borderRight: `1px dashed ${U.grid}` }}>
                {STROKES.map(([k, lab]) => {
                  const active = stroke === k;
                  return (
                    <button
                      key={k}
                      title={lab}
                      aria-pressed={active}
                      onClick={() => setStroke(k)}
                      style={{
                        width: 118, height: 52, borderRadius: 13, cursor: 'pointer', transition: 'all .15s ease',
                        backgroundImage: `url("${strokeIconUrl(k)}")`,
                        backgroundRepeat: 'no-repeat', backgroundPosition: 'center', backgroundSize: 'auto 36px',
                        border: `1.5px solid ${active ? adultAccent : U.cardBorder}`,
                        backgroundColor: active ? C.adult.soft : U.subtle,
                        boxShadow: active ? `0 0 0 2px ${adultAccent}33` : undefined,
                      }}
                    >
                      <span className="sr-only">{lab}</span>
                    </button>
                  );
                })}
              </div>
              {/* Дистанции — колонка справа */}
              <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
                {allDistanceKeys.map((dv) => {
                  const active = effDist === dv;
                  return (
                    <button
                      key={dv}
                      aria-pressed={active}
                      onClick={() => { setDist(dv); setOthersOpen(false); }}
                      style={{
                        textAlign: 'center', padding: '9px 18px', borderRadius: 11, cursor: 'pointer',
                        fontSize: 13, fontWeight: 800, transition: 'all .15s ease',
                        border: `1.5px solid ${active ? adultAccent : U.cardBorder}`,
                        background: active ? adultAccent : U.subtle,
                        color: active ? '#fff' : U.text,
                      }}
                    >
                      {dv}
                    </button>
                  );
                })}
              </div>
            </div>
          )}
        </div>
      </div>

      {/* ===== Возрастные группы (masters) ===== */}
      {isMastersBool && availableAgeGroups.length > 0 && (
        <div className="mt-3">
          <div style={{ fontSize: 11, fontWeight: 800, letterSpacing: '.06em', color: U.label, textTransform: 'uppercase', marginBottom: 6 }}>
            Age Group
          </div>
          <div className="flex gap-2 flex-wrap">
            {availableAgeGroups.map((ag) => {
              const active = selectedAgeGroup === ag;
              return (
                <button
                  key={ag}
                  onClick={() => setSelectedAgeGroup(ag)}
                  aria-pressed={active}
                  style={{
                    fontSize: 12, fontWeight: 700, padding: '6px 13px', borderRadius: 9,
                    cursor: 'pointer', transition: 'all .18s ease',
                    border: `1.5px solid ${active ? adultAccent : U.cardBorder}`,
                    background: active ? adultAccent : U.subtle,
                    color: active ? '#fff' : U.text,
                  }}
                >
                  {ag}
                </button>
              );
            })}
          </div>
        </div>
      )}

      {/* ============ DESKTOP: матрица нормативов ============ */}
      {!isMobile && (
        <>
          <div className="overflow-x-auto" style={{ marginTop: 10 }}>
            <table style={{ borderCollapse: 'separate', borderSpacing: 0, width: '100%', minWidth: 900 }}>
              <thead>
                <tr>
                  <th
                    style={{
                      position: 'sticky', left: 0, zIndex: 3, background: 'var(--theme-mode-surface)',
                      textAlign: 'left', padding: '10px 14px', fontSize: 11, fontWeight: 800,
                      letterSpacing: '.06em', color: U.label, textTransform: 'uppercase',
                      borderBottom: `1px solid ${U.grid}`,
                    }}
                  >
                    Distance
                  </th>
                  {(['WR', 'NR'] as const).map((kind) => (
                    <th key={kind} style={{ padding: '8px 8px 10px', borderBottom: `1px solid ${U.grid}`, verticalAlign: 'bottom' }}>
                      <div className="nrm-med" style={medStyle(C.rec)}>{kind === 'WR' ? 'WR' : HOME_REGION}</div>
                      <div style={{ fontSize: 9, fontWeight: 700, color: U.label, marginTop: 4, textAlign: 'center', letterSpacing: '.04em' }}>
                        {kind === 'WR' ? 'World' : HOME_REGION_LABEL}
                      </div>
                      {recordsUpdatedLabel && (
                        <div style={{ fontSize: 8, fontWeight: 600, color: U.label, marginTop: 2, textAlign: 'center', opacity: 0.75 }}>
                          updated {recordsUpdatedLabel}
                        </div>
                      )}
                    </th>
                  ))}
                  {ladder.map((lv, i) => {
                    const cc = C[lv.cat];
                    const isYou = i === myIdx;
                    return (
                      <th key={lv.key} title={lv.key} style={{ padding: '8px 6px 10px', borderBottom: `1px solid ${U.grid}`, verticalAlign: 'bottom' }}>
                        {/* Слот бейджа всегда занимает место — медальоны на одной линии */}
                        <div style={{ height: 15, display: 'flex', alignItems: 'center', justifyContent: 'center', marginBottom: 5 }}>
                          {isYou && (
                            <span
                              style={{
                                fontSize: 8, fontWeight: 900, letterSpacing: '.08em', color: cc.accent,
                                background: cc.soft, padding: '2px 6px', borderRadius: 5, border: `1px solid ${cc.border}`,
                              }}
                            >
                              YOU
                            </span>
                          )}
                        </div>
                        <div className="nrm-med" style={medStyle(cc)}>{lv.label}</div>
                      </th>
                    );
                  })}
                </tr>
              </thead>
              <tbody>
                {allDistanceKeys.map((d) => {
                  const isYouRow = d === effDist;
                  return (
                    <tr key={d}>
                      <td
                        style={{
                          position: 'sticky', left: 0, zIndex: 2, padding: '11px 14px',
                          borderBottom: `1px solid ${U.grid}`, color: U.text,
                          background: isYouRow ? U.rowWash : 'var(--theme-mode-surface)',
                          borderLeft: isYouRow ? `3px solid ${adultAccent}` : undefined,
                        }}
                      >
                        <div style={{ fontSize: 15, fontWeight: 800 }}>{d}</div>
                        {isYouRow && (
                          <div style={{ fontSize: 9, fontWeight: 800, letterSpacing: '.05em', color: adultAccent, textTransform: 'uppercase', marginTop: 2 }}>
                            your event
                          </div>
                        )}
                      </td>
                      {(['WR', 'NR'] as const).map((kind) => (
                        <td
                          key={kind}
                          style={{
                            padding: '7px 7px', borderBottom: `1px solid ${U.grid}`,
                            background: isYouRow ? U.rowWashSoft : 'transparent',
                          }}
                        >
                          <div style={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
                            {(['male', 'female'] as Gender[]).map((g) => {
                              const rec = fmtRecord(g, d, kind);
                              const updatedOne = maxUpdatedAtLabel([rec.updatedAt]);
                              const titleParts = [rec.holder, updatedOne ? `updated ${updatedOne}` : null].filter(Boolean);
                              return (
                                <div
                                  key={g}
                                  style={g === 'male' ? menPillStyle : womenPillStyle}
                                  title={titleParts.length ? titleParts.join(' · ') : undefined}
                                >
                                  {rec.text}
                                </div>
                              );
                            })}
                          </div>
                        </td>
                      ))}
                      {ladder.map((lv, i) => {
                        const cc = C[lv.cat];
                        // Единственный светящийся элемент матрицы — пересечение
                        // (дистанция пловца × его текущий уровень)
                        const cross = isYouRow && i === myIdx;
                        const tdStyle: React.CSSProperties = {
                          padding: '7px 6px',
                          borderBottom: `1px solid ${U.grid}`,
                          background: isYouRow ? U.rowWashSoft : undefined,
                        };
                        if (cross) {
                          tdStyle.background = cc.soft;
                          (tdStyle as any)['--gc'] = cc.accent;
                        }
                        return (
                          <td key={lv.key} className={`nrm-cell${cross ? ' nrm-glow-cell' : ''}`} style={tdStyle}>
                            <div style={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
                              <div style={{ ...menPillStyle, ...(cross ? { fontWeight: 900 } : null) }}>
                                {fmt(maleStyle[d]?.[lv.key])}
                              </div>
                              <div style={{ ...womenPillStyle, ...(cross ? { fontWeight: 900 } : null) }}>
                                {fmt(femaleStyle[d]?.[lv.key])}
                              </div>
                            </div>
                          </td>
                        );
                      })}
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>

          {/* Легенда — под таблицей */}
          <div className="flex items-center gap-2 flex-wrap" style={{ padding: '12px 0 4px', fontSize: 12, color: U.label, fontWeight: 600 }}>
            In each cell:
            <span className="inline-flex items-center gap-1.5">
              <span style={{ width: 22, height: 14, borderRadius: 4, background: U.menPill }} /> top = <b style={{ color: U.menText }}>Men</b>
            </span>
            <span className="inline-flex items-center gap-1.5">
              <span style={{ width: 22, height: 14, borderRadius: 4, background: U.womenPill }} /> bottom = <b style={{ color: U.womenText }}>Women</b>
            </span>
          </div>
        </>
      )}

      {/* ============ MOBILE: сравнение текущего и следующего уровня ============ */}
      {isMobile && leftLevel && rightLevel && (
        <div style={{ marginTop: 12 }}>

          {/* Пейджер по лестнице уровней */}
          <div className="flex items-center gap-2.5" style={{ marginBottom: 12 }}>
            <button
              className="nrm-pager-btn"
              onClick={goPrev}
              disabled={!canPrev}
              aria-label="Slower level"
              style={{
                width: 40, height: 40, borderRadius: 11, display: 'flex', alignItems: 'center', justifyContent: 'center',
                fontSize: 22, fontWeight: 700, border: 'none', background: U.subtle,
                color: canPrev ? U.text : U.label, opacity: canPrev ? 1 : 0.4, cursor: canPrev ? 'pointer' : 'default',
              }}
            >
              ‹
            </button>
            <div style={{ flex: 1, textAlign: 'center', fontSize: 11, fontWeight: 800, letterSpacing: '.08em', color: U.label, textTransform: 'uppercase' }}>
              Level → faster
            </div>
            <button
              className="nrm-pager-btn"
              onClick={goNext}
              disabled={!canNext}
              aria-label="Faster level"
              style={{
                width: 40, height: 40, borderRadius: 11, display: 'flex', alignItems: 'center', justifyContent: 'center',
                fontSize: 22, fontWeight: 700, border: 'none', background: U.subtle,
                color: canNext ? U.text : U.label, opacity: canNext ? 1 : 0.4, cursor: canNext ? 'pointer' : 'default',
              }}
            >
              ›
            </button>
          </div>

          {/* Пара карточек: слева текущий (фокус), справа следующий (быстрее).
              key = pIdx → remount → анимация слайда replay-ится */}
          <div
            key={pIdx}
            className={navDir === 'next' ? 'nrm-slide-next' : navDir === 'prev' ? 'nrm-slide-prev' : ''}
            style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 12 }}
          >
            {[
              { level: leftLevel, isNext: false },
              { level: rightLevel, isNext: true },
            ].map(({ level, isNext }) => {
              const cc = C[level.cat];
              const isYou = level.key.toLowerCase() === (levelName || '').toLowerCase();
              const leftSec = normToSeconds(maleStyle[effDist]?.[leftLevel.key]);
              const rightSec = normToSeconds(maleStyle[effDist]?.[rightLevel.key]);
              const leftSecW = normToSeconds(femaleStyle[effDist]?.[leftLevel.key]);
              const rightSecW = normToSeconds(femaleStyle[effDist]?.[rightLevel.key]);
              const deltaMen = Number.isFinite(leftSec) && Number.isFinite(rightSec) ? (leftSec - rightSec).toFixed(2) : null;
              const deltaWomen = Number.isFinite(leftSecW) && Number.isFinite(rightSecW) ? (leftSecW - rightSecW).toFixed(2) : null;
              return (
                <div
                  key={level.key}
                  style={{
                    display: 'flex', flexDirection: 'column', alignItems: 'center',
                    padding: '14px 12px 16px', borderRadius: 16, background: U.card,
                    border: `2px solid ${isNext ? U.cardBorder : cc.accent}`,
                    boxShadow: isNext ? undefined : `0 0 0 1px ${cc.accent}, 0 8px 26px -8px ${cc.accent}`,
                  }}
                >
                  <span
                    style={{
                      alignSelf: 'flex-start', fontSize: 9, fontWeight: 900, letterSpacing: '.07em',
                      padding: '3px 8px', borderRadius: 6, marginBottom: 10,
                      ...(isNext
                        ? { color: U.label, background: U.subtle, border: `1px solid ${U.cardBorder}` }
                        : { color: '#fff', background: cc.accent }),
                    }}
                  >
                    {isNext ? 'NEXT →' : isYou ? 'YOU' : 'CURRENT'}
                  </span>
                  <div
                    className="nrm-med"
                    style={{ ...medStyle(cc), minWidth: 60, height: 44, fontSize: 18, opacity: isNext ? 0.85 : 1 }}
                  >
                    {level.label}
                  </div>
                  <div style={{ width: '100%', marginTop: 12, display: 'flex', flexDirection: 'column', gap: 7 }}>
                    {(['male', 'female'] as Gender[]).map((g) => {
                      const men = g === 'male';
                      const time = fmt((men ? maleStyle : femaleStyle)[effDist]?.[level.key]);
                      const delta = isNext ? (men ? deltaMen : deltaWomen) : null;
                      const color = men ? U.menText : U.womenText;
                      return (
                        <div
                          key={g}
                          style={{
                            display: 'flex', alignItems: 'center', justifyContent: 'space-between',
                            background: men ? U.menPill : U.womenPill, borderRadius: 9, padding: '7px 11px',
                          }}
                        >
                          <span style={{ fontSize: 10, fontWeight: 800, color }}>{men ? '♂' : '♀'}</span>
                          {/* min-height выравнивает времена в обеих карточках при наличии дельты */}
                          <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'flex-end', justifyContent: 'center', lineHeight: 1.02, minHeight: 30 }}>
                            <span style={{ fontSize: 17, fontWeight: 800, color, fontVariantNumeric: 'tabular-nums' }}>{time}</span>
                            {delta && (
                              <span style={{ fontSize: 10, fontWeight: 700, color, opacity: 0.68, fontVariantNumeric: 'tabular-nums' }}>
                                −{delta}
                              </span>
                            )}
                          </div>
                        </div>
                      );
                    })}
                  </div>
                </div>
              );
            })}
          </div>

          {/* Остальные дистанции — коллапс */}
          <button
            onClick={() => setOthersOpen((o) => !o)}
            aria-expanded={othersOpen}
            style={{
              width: '100%', marginTop: 14, display: 'flex', alignItems: 'center', justifyContent: 'space-between',
              cursor: 'pointer', padding: '12px 14px', background: U.subtle, borderRadius: 13, border: 'none',
            }}
          >
            <span style={{ fontSize: 13, fontWeight: 800, color: U.text }}>Other distances</span>
            <span style={{ fontSize: 11, color: U.label, transform: othersOpen ? 'rotate(180deg)' : 'rotate(0deg)', transition: 'transform .2s ease' }}>
              ▼
            </span>
          </button>
          {othersOpen && (
            <div style={{ marginTop: 8, display: 'flex', flexDirection: 'column', gap: 8 }}>
              {allDistanceKeys.filter((d) => d !== effDist).map((d) => (
                <div key={d} style={{ background: U.card, border: `1px solid ${U.cardBorder}`, borderRadius: 12, padding: '10px 12px' }}>
                  <div style={{ fontSize: 13, fontWeight: 800, color: U.text, marginBottom: 7 }}>{d}</div>
                  <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 8 }}>
                    {[leftLevel, rightLevel].map((level, i) => {
                      const cc = C[level.cat];
                      return (
                        <div key={level.key} style={{ border: `1px solid ${cc.border}`, borderRadius: 9, padding: '7px 9px' }}>
                          <div style={{ fontSize: 9, fontWeight: 800, letterSpacing: '.04em', color: cc.accent, textTransform: 'uppercase', marginBottom: 5 }}>
                            {level.label}{i === 1 ? ' →' : ''}
                          </div>
                          <div style={{ display: 'flex', gap: 8 }}>
                            <span style={{ fontSize: 13, fontWeight: 700, color: U.menText, fontVariantNumeric: 'tabular-nums' }}>
                              {fmt(maleStyle[d]?.[level.key])}
                            </span>
                            <span style={{ fontSize: 13, fontWeight: 700, color: U.womenText, fontVariantNumeric: 'tabular-nums' }}>
                              {fmt(femaleStyle[d]?.[level.key])}
                            </span>
                          </div>
                        </div>
                      );
                    })}
                  </div>
                </div>
              ))}
            </div>
          )}
        </div>
      )}
    </div>
  );
};

export default PopupContentNormative;
