import React from 'react';
import { lineDir } from '../../workout-format';
import { PrintIntensity } from '../parts/print-intensity';
import { PrintRichText } from '../parts/print-rich-text';
import { PrintSetNo } from '../parts/print-set-no';
import { PrintText } from '../parts/print-text';
import type { SheetCube, SheetLine } from '../print-types';
import { ROW_BELOW_ATTR, ROW_DIST_ATTR, ROW_LINE_ATTR, ROW_SET_ATTR, ROW_TEXT_ATTR } from '../rows-fit-measure';
import { FIT_X_ATTR } from '../sheet-fit-measure';
import { rowsLineKey, setHasTitleRow } from './rows-pages';

/**
 * Сет «листа строк» (хендофф 27.11 §3): рейка (номер в кружке + метры) и строки «дистанция · текст ·
 * время». Колонки — общие на весь лист: сет и строка — `subgrid` сетки `.wprw-body`, поэтому
 * дистанции и времена стоят столбиками по всему листу. Стили — `rows-sheet.css`.
 */

const RowsLine: React.FC<{ line: SheetLine; lineKey: string; below: boolean }> = ({ line, lineKey, below }) => {
  const { rich } = line;
  const hasText = rich.parts.length > 0 || !!rich.chips || !!line.intensityText;
  return (
    <div className="wprw-line" {...{ [ROW_LINE_ATTR]: lineKey }} {...(below ? { [ROW_BELOW_ATTR]: '' } : {})}>
      {line.head && (
        <div className="wprw-dist" {...{ [ROW_DIST_ATTR]: '' }}>
          <bdi dir="ltr">{line.head}</bdi>
        </div>
      )}
      {hasText && (
        // FIT_X: слово шире колонки и после правила «под дистанцией» — лист не влез.
        <div className={`wprw-text${line.head ? '' : ' is-wide'}`} {...{ [ROW_TEXT_ATTR]: '', [FIT_X_ATTR]: '' }}>
          <PrintRichText rich={rich} chipClassName="wprw-chip wprw-chip-in-text" nowrapClassName="wprw-nowrap" />
          <PrintIntensity
            chips={rich.chips}
            text={line.intensityText}
            className="wprw-chips"
            chipClassName="wprw-chip"
            textClassName="wprw-intensity-text"
          />
        </div>
      )}
      {line.time && (
        <div className="wprw-time">
          <bdi dir="ltr">{line.time}</bdi>
        </div>
      )}
    </div>
  );
};

export const RowsSet: React.FC<{ cube: SheetCube; ruled: boolean; below: ReadonlySet<string> }> = ({ cube, ruled, below }) => {
  const title = setHasTitleRow(cube);
  const rows = Math.max(1, (title ? 1 : 0) + cube.lines.length);
  return (
    <section className={`wprw-set${cube.main ? ' is-main' : ''}${ruled ? ' is-ruled' : ''}`} {...{ [ROW_SET_ATTR]: '' }}>
      <div className="wprw-rail" style={{ gridRow: `1 / span ${rows}` }}>
        <PrintSetNo n={cube.no} className="wprw-num" />
        {cube.metersLabel && <bdi dir="ltr" className="wprw-meters">{cube.metersLabel}</bdi>}
      </div>
      {title && (
        <div className="wprw-title" {...{ [FIT_X_ATTR]: '' }}>
          {cube.repeat > 1 && <bdi dir="ltr" className="wprw-repeat">×{cube.repeat}</bdi>}
          {cube.ownTitle && <bdi dir={lineDir(cube.ownTitle)}><PrintText text={cube.ownTitle} /></bdi>}
          {cube.main && <bdi dir="ltr" className="wprw-chip wprw-main-chip">Main</bdi>}
        </div>
      )}
      {cube.lines.map((line, i) => {
        const key = rowsLineKey(cube, i);
        return <RowsLine key={key} line={line} lineKey={key} below={below.has(key)} />;
      })}
    </section>
  );
};
