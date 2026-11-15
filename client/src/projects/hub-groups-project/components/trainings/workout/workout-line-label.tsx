import React from 'react';
import type { WorkoutLine } from '../../../types';
import { intensityKeys, lineDir, repsDistanceLabel } from './workout-format';
import UI_IntensityIcon from '../../../../components/mix/intensity-icon/intensity-icon';

/**
 * Подпись строки workout «4×100 free · 2-3V» для вида только для чтения (CF6–CF7). Числа и текст
 * тренера — разными изолированными кусками: в ивритской строке одной строкой «4×150» алгоритм bidi
 * переставлял в «150×4» (знак × нейтральный, числа — отдельные прогоны), а «2-3V» — в «V2-3».
 * Направление — явное (`lineDir`): ивритская строка идёт справа налево, как на листе тренера;
 * `dir="auto"` изоляты не видит и ставил LTR. Строку-контейнер с временем выхода вызывающий
 * разворачивает тем же `lineDir`, чтобы время ушло влево.
 * Строковая версия для подсказок и свёрнутых строк — `lineSummary` (`workout-format.ts`).
 */
function WorkoutLineLabel({ line, reps = line.reps }: { line: WorkoutLine; reps?: number }) {
  const head = repsDistanceLabel(reps, line.distance);
  const dir = lineDir(line.text);
  return (
    <span dir={dir} className="min-w-0">
      {head && <bdi dir="ltr" className="hp-mono font-extrabold">{head}</bdi>}
      {head && line.text ? ' ' : ''}
      {line.text && <bdi dir={dir}>{line.text}</bdi>}
      {line.intensity && <>{' '}<Intensity raw={line.intensity} /></>}
    </span>
  );
}

/**
 * Нагрузка строки — тот же чип шкалы V0–V5, что в таблице времён тренировок (`UI_IntensityIcon`:
 * цвет по уровню, нажатие открывает шкалу). «2-3V» — два чипа. Не по шкале — текстом, как раньше.
 */
function Intensity({ raw }: { raw: string }) {
  const keys = intensityKeys(raw);
  if (!keys) return <bdi dir="ltr" className="hp-mono">· {raw}</bdi>;
  return (
    <bdi dir="ltr" className="inline-flex gap-0.5 align-middle">
      {keys.map((k) => <UI_IntensityIcon key={k} intensity={k} />)}
    </bdi>
  );
}

export default WorkoutLineLabel;
