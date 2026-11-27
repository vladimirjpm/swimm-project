import React from 'react';
import { lineDir } from '../../workout-format';
import type { SheetCube, SheetDir, SheetLine } from '../print-types';
import { FIT_X_ATTR } from '../sheet-fit-measure';
import { PrintIntensity } from './print-intensity';
import { PrintRichText } from './print-rich-text';
import { PrintSetNo } from './print-set-no';
import { PrintText } from './print-text';

/**
 * Куб сета на листе печати (§3.2): рамка, номер по порядку выполнения в кружке, шапка «2× название»
 * с чипом `Main` и метрами на другой стороне, строки. Общая деталь — её возьмут и другие виды.
 *
 * Строка раскладывается по направлению ЛИСТА (§3.6): подпись со стороны начала, ячейка уровня — с
 * противоположной, одной ширины на весь лист (`cellChars`, моноширинные цифры — времена стоят
 * столбиком). Внутри подписи — изоляты, как в `WorkoutLineLabel`: «4×150» не станет «150×4».
 *
 * Уровни нагрузки в тексте тренера — чипами, как на листе строк (В19, Влад 27.11.2026: «4V» → [V4],
 * «1-3V» → «1-[V3]»); чипы поля `intensity` — только если уровня в тексте нет (`SheetLine.rich`).
 */
const PrintLine: React.FC<{ line: SheetLine; cellChars: number }> = ({ line, cellChars }) => {
  const { rich } = line;
  return (
    <div className="wpl-line">
      {/* FIT_X: слово шире подписи наезжает на ячейку внутри куба — сам куб этого не видит. */}
      <span className="wpl-label" {...{ [FIT_X_ATTR]: '' }}>
        {line.head && <bdi dir="ltr" className="wpl-reps">{line.head}</bdi>}
        {line.head && line.text ? ' ' : ''}
        {line.text && <PrintRichText rich={rich} chipClassName="wpl-chip wpl-chip-in-text" nowrapClassName="wpl-nowrap" />}
        {(rich.chips || line.intensityText) && ' '}
        <PrintIntensity chips={rich.chips} text={line.intensityText} />
      </span>
      {cellChars > 0 && (
        <bdi dir="ltr" className="wpl-cell" style={{ width: `${cellChars}ch` }}>
          {line.cell}
        </bdi>
      )}
    </div>
  );
};

export const PrintSetCube: React.FC<{
  cube: SheetCube;
  dir: SheetDir;
  cellChars: number;
  style?: React.CSSProperties;
}> = ({ cube, dir, cellChars, style }) => (
  <section className="wpl-cube" dir={dir} style={style} {...{ [FIT_X_ATTR]: '' }}>
    <header className="wpl-cube-head">
      {/* Номер внутри названия: при переносе длинного названия он не остаётся один в строке. */}
      <span className="wpl-cube-title" {...{ [FIT_X_ATTR]: '' }}>
        <PrintSetNo n={cube.no} />{' '}
        {cube.repeat > 1 && <bdi dir="ltr">{cube.repeat}×</bdi>}
        {cube.repeat > 1 && ' '}
        <bdi dir={lineDir(cube.title)}><PrintText text={cube.title} /></bdi>
      </span>
      {cube.main && <span className="wpl-chip wpl-main-chip">Main</span>}
      {/* Распорка, а не margin-inline-start у метров: у `<bdi dir="ltr">` «начало» — его собственное. */}
      <span className="wpl-spacer" />
      {cube.metersLabel && <bdi dir="ltr" className="wpl-cube-meters">{cube.metersLabel}</bdi>}
    </header>
    {cube.lines.map((line, i) => <PrintLine key={i} line={line} cellChars={cellChars} />)}
  </section>
);
