import { useEffect, useMemo, useState } from 'react';
import { useAppSelector } from '../../../../store/store';
import Helper from '../../../../utils/helpers/data-helper';
import RecordsHelper, { maxUpdatedAtLabel } from '../../../../utils/helpers/records-helper';
import {
  type PoolType, type Gender, type DistancesMap, type PoolMapLoose, type LevelsMap,
  type Normatives, type RecordsTree, type RecordCellMin,
  derivePoolType, deriveStroke, normalizeDistance, getRecordCell, getMastersRecordCell,
  LADDER, LEVEL_PRIORITY, LEVEL_LABELS, catOfLevel,
} from './normative-model';

/**
 * Данные попапа нормативов из `popUpObj`: что выбрано (бассейн, стиль, дистанция, возрастная
 * полоса мастерсов), какие нормативы и рекорды показывать, лестница уровней и место пловца в ней.
 */
// Данные приходят из RecordsHelper (БД, /api/records + /api/normative-standards);
// до загрузки — пустое дерево. NR = национальный рекорд HOME_REGION (см. records-helper.ts).
export const useNormativeTable = () => {
  const { levelName = '', styleName = '', styleLen = '', poolLen = '', poolType: popUpPoolType, isMasters = false, normativeAgeGroup } =
    useAppSelector((state) => state.popUpObj);

  // Приведение isMasters к булеву типу (строгое сравнение + строка/число)
  const isMastersBool = isMasters === true || isMasters === 'true' || isMasters === 1;

  // Данные рекордов/нормативов — читаем на каждый рендер (RecordsHelper может догрузиться позже)
  const norms = RecordsHelper.getStandards() as unknown as Normatives;
  const normsMasters = RecordsHelper.getMastersStandards() as unknown as Normatives | null;
  const recordsTree = RecordsHelper.getOpenRecords() as unknown as RecordsTree;

  const [poolType, setPoolType] = useState<PoolType>(derivePoolType(popUpPoolType ?? poolLen));
  const [stroke, setStroke] = useState<string>(deriveStroke(styleName));
  // selectedAgeGroup только если masters-режим, иначе пусто
  const [selectedAgeGroup, setSelectedAgeGroup] = useState<string>(isMastersBool ? (normativeAgeGroup ?? '25-29') : '');
  // Дистанция пловца — стартует со styleLen, меняется чипами в дропдауне
  const [dist, setDist] = useState<string>(() => normalizeDistance(styleLen));

  // Мастерские деревья рекордов (свой + мировой) грузятся асинхронно: RecordsHelper.warmUp()
  // мог не успеть к открытию попапа, и геттеры до загрузки отдают пустое дерево. Подписки у
  // RecordsHelper нет, поэтому опрашиваем: раз в 300 мс тик перерисовки, пока оба дерева не
  // придут. ⚠ Опрос ОГРАНИЧЕН (~6 с): упала загрузка — дерево пустое навсегда, и без предела
  // интервал крутился бы всё время, пока открыт попап. Не дождались — колонки остаются «—».
  const [recordsTick, setRecordsTick] = useState(0);
  useEffect(() => {
    if (!isMastersBool) return undefined;
    const ready = () =>
      Object.keys(RecordsHelper.getMastersRecords().normatives).length > 0 &&
      Object.keys(RecordsHelper.getWorldMastersRecords().normatives).length > 0;
    if (ready()) return undefined;
    let tries = 0;
    const id = window.setInterval(() => {
      setRecordsTick((t) => t + 1);
      if (ready() || ++tries >= 20) window.clearInterval(id);
    }, 300);
    return () => window.clearInterval(id);
  }, [isMastersBool]);

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

  // В режиме мастерсов с выбранной полосой берём рекорд ЭТОЙ полосы (WR — мировой мастерский,
  // NR — израильский мастерский); иначе — прежние абсолютные open-рекорды. Решения 1-2 этапа 11.4.
  const useMastersBandRecords = isMastersBool && !!selectedAgeGroup;
  // Одно место выбора клетки — на ячейку таблицы и на подпись «updated»: разойдутся — подпись
  // будет говорить про одни рекорды, а таблица показывать другие.
  const pickRecordCell = (g: Gender, d: string, kind: 'WR' | 'NR'): RecordCellMin | null =>
    useMastersBandRecords
      ? getMastersRecordCell(
          kind === 'WR' ? RecordsHelper.getWorldMastersRecords() : RecordsHelper.getMastersRecords(),
          g, poolType, stroke, d, selectedAgeGroup,
        )
      : getRecordCell(recordsTree, g, poolType, stroke, d, kind);
  const fmtRecord = (g: Gender, d: string, kind: 'WR' | 'NR') => {
    const cell = pickRecordCell(g, d, kind);
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
          stamps.push(pickRecordCell(g, d, kind)?.updated_at);
        });
      });
    });
    return maxUpdatedAtLabel(stamps);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [recordsTree, poolType, stroke, allDistanceKeys.join(','), useMastersBandRecords, selectedAgeGroup, recordsTick]);

  return {
    levelName, isMastersBool,
    poolType, setPoolType,
    stroke, setStroke,
    selectedAgeGroup, setSelectedAgeGroup,
    setDist,
    availableAgeGroups,
    maleStyle, femaleStyle,
    allDistanceKeys, effDist,
    ladder, myIdx,
    useMastersBandRecords, fmtRecord, recordsUpdatedLabel,
  };
};
