import React from 'react';
import type { WorkoutLine } from '../../../types';
import { repsDistanceLabel } from './workout-format';

/**
 * Подпись строки workout «4×100 free · 2-3V» для вида только для чтения (CF6–CF7). Числа и текст
 * тренера — разными изолированными кусками: в ивритской строке одной строкой «4×150» алгоритм bidi
 * переставлял в «150×4» (знак × нейтральный, числа — отдельные прогоны), а «2-3V» — в «V2-3».
 * Строковая версия для подсказок и свёрнутых строк — `lineSummary` (`workout-format.ts`).
 */
function WorkoutLineLabel({ line, reps = line.reps }: { line: WorkoutLine; reps?: number }) {
  const head = repsDistanceLabel(reps, line.distance);
  return (
    <span dir="auto" className="min-w-0">
      {head && <bdi dir="ltr" className="hp-mono font-extrabold">{head}</bdi>}
      {head && line.text ? ' ' : ''}
      {line.text && <bdi>{line.text}</bdi>}
      {line.intensity && <>{' · '}<bdi dir="ltr" className="hp-mono">{line.intensity}</bdi></>}
    </span>
  );
}

export default WorkoutLineLabel;
