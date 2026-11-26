import React from 'react';
import { PrintArrow } from '../parts/print-arrow';
import { PrintSetCube } from '../parts/print-set-cube';
import { ARROW_GAP_EM, cubeGrid, type CubeColumns } from '../print-fit';
import type { SheetModel } from '../print-types';

/**
 * Раскладка «кубы со стрелками» вида `large` (§3.4, В12–В15): кубы по сетке листа в порядке
 * выполнения «змейкой» (ивритский лист — от верхнего правого угла), между соседями стрелки.
 *
 * Одна CSS-сетка: колонки кубов чередуются с промежутками 1.6em под горизонтальные стрелки, ряды
 * кубов — с рядами 1.6em под стрелку вниз. Сетка всегда `ltr`: `cubeGrid` отдаёт ФИЗИЧЕСКИЕ колонки
 * слева направо. Высота ряда — по самому высокому кубу, остальные тянутся до неё (stretch сетки).
 */
export const LargeCubesLayout: React.FC<{ model: SheetModel; columns: CubeColumns }> = ({ model, columns }) => {
  const grid = cubeGrid(model.cubes.length, columns, model.dir);
  const gap = `${ARROW_GAP_EM}em`;
  const style: React.CSSProperties = {
    gridTemplateColumns: Array.from({ length: columns }, () => 'minmax(0, 1fr)').join(` ${gap} `),
    gridTemplateRows: grid.rows.map(() => 'auto').join(` ${gap} `),
  };
  return (
    <div className="wpl-cubes" dir="ltr" style={style}>
      {grid.rows.flatMap((row, r) =>
        row.map((idx, c) =>
          idx == null ? null : (
            <PrintSetCube
              key={model.cubes[idx].no}
              cube={model.cubes[idx]}
              dir={model.dir}
              cellChars={model.cellChars}
              style={{ gridRow: 2 * r + 1, gridColumn: 2 * c + 1 }}
            />
          ),
        ),
      )}
      {grid.arrows.map((a) =>
        a.kind === 'down' ? (
          <div key={`a${a.from}`} className="wpl-arrow-down" style={{ gridRow: 2 * a.row + 2, gridColumn: 2 * a.col + 1 }}>
            <PrintArrow dir="down" />
          </div>
        ) : (
          <div key={`a${a.from}`} className="wpl-arrow-h" style={{ gridRow: 2 * a.row + 1, gridColumn: 2 * a.col + 2 }}>
            <PrintArrow dir={a.kind} />
          </div>
        ),
      )}
    </div>
  );
};
