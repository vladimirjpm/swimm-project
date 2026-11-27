import React from 'react';
import { lineDir } from '../../workout-format';
import { PrintChip, PrintIntensity } from '../parts/print-intensity';
import { PrintRichText } from '../parts/print-rich-text';
import { PrintSetNo } from '../parts/print-set-no';
import { PrintText } from '../parts/print-text';
import type { SheetCube, SheetLine } from '../print-types';
import { NUM_BELOW_ATTR, NUM_DIST_ATTR, NUM_LINE_ATTR, NUM_SET_ATTR, NUM_TEXT_ATTR } from '../numbers-fit-measure';
import { FIT_X_ATTR } from '../sheet-fit-measure';
import { lineTextSpan, numbersLineKey } from './numbers-pages';

/**
 * Куб «листа кубов» (хендофф 27.11 вторая версия, §3): подпись куба (номер в кружке · «×2» · слово
 * главного сета в рамке · название · метры у дальнего края) и строки «дистанция · текст · время».
 * Колонки — общие на весь лист: куб и строка — `subgrid` сетки `.wprn-body`, поэтому дистанции и
 * времена стоят столбиками по всему листу. Стили — `numbers-sheet.css`.
 */

const NumbersLine: React.FC<{ line: SheetLine; lineKey: string; below: boolean }> = ({ line, lineKey, below }) => {
  const { rich } = line;
  const hasText = rich.parts.length > 0 || !!rich.chips || !!line.intensityText;
  const { wide, toEnd } = lineTextSpan(line);
  const span = `${wide ? ' is-wide' : ''}${toEnd ? ' is-to-end' : ''}`;
  return (
    <div className="wprn-line" {...{ [NUM_LINE_ATTR]: lineKey }} {...(below ? { [NUM_BELOW_ATTR]: '' } : {})}>
      {line.head && (
        <div className="wprn-dist" {...{ [NUM_DIST_ATTR]: '' }}>
          <bdi dir="ltr">{line.head}</bdi>
        </div>
      )}
      {hasText && (
        // FIT_X: слово шире колонки и после правила «под дистанцией» — лист не влез.
        <div className={`wprn-text${span}`} {...{ [NUM_TEXT_ATTR]: '', [FIT_X_ATTR]: '' }}>
          <PrintRichText rich={rich} chipClassName="wprn-chip wprn-chip-in-text" nowrapClassName="wprn-nowrap" />
          <PrintIntensity
            chips={rich.chips}
            text={line.intensityText}
            className="wprn-chips"
            chipClassName="wprn-chip"
            textClassName="wprn-intensity-text"
          />
        </div>
      )}
      {line.time && (
        <div className="wprn-time">
          <bdi dir="ltr">{line.time}</bdi>
        </div>
      )}
    </div>
  );
};

export const NumbersSet: React.FC<{ cube: SheetCube; below: ReadonlySet<string> }> = ({ cube, below }) => (
  <section className={`wprn-set${cube.main ? ' is-main' : ''}`} {...{ [NUM_SET_ATTR]: '' }}>
    {/* Подпись куба есть всегда; «Set N» не печатается — у сета без названия только номер и метры. */}
    <div className="wprn-set-head" {...{ [FIT_X_ATTR]: '' }}>
      <PrintSetNo n={cube.no} className="wprn-num" />
      {cube.repeat > 1 && <bdi dir="ltr" className="wprn-repeat">×{cube.repeat}</bdi>}
      {cube.mainWord && <PrintChip label={cube.mainWord} className="wprn-chip wprn-main-word" dir={lineDir(cube.mainWord)} />}
      {cube.titleRest && (
        <bdi dir={lineDir(cube.titleRest)} className="wprn-title"><PrintText text={cube.titleRest} /></bdi>
      )}
      {/* Распорка, а не margin-inline-start у метров: у `<bdi dir="ltr">` «начало» — его собственное. */}
      <span className="wprn-spacer" />
      {cube.metersLabel && <bdi dir="ltr" className="wprn-metres">{cube.metersLabel}</bdi>}
    </div>
    {cube.lines.map((line, i) => {
      const key = numbersLineKey(cube, i);
      return <NumbersLine key={key} line={line} lineKey={key} below={below.has(key)} />;
    })}
  </section>
);
