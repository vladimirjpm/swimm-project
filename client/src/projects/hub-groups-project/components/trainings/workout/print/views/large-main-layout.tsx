import React from 'react';
import { PrintArrow } from '../parts/print-arrow';
import { PrintSetCube } from '../parts/print-set-cube';
import { PrintSetNo } from '../parts/print-set-no';
import type { MainLayoutModel, SheetModel } from '../print-types';

/**
 * Раскладка «главный справа» вида `large` (§3.3, В2): правая колонка — главные сеты, левая —
 * остальные в порядке листа; на месте главных в левой колонке — указатель «④ ──▶» к правой (В14).
 * «Правая» — физически правая сторона листа при любом направлении текста: сетка колонок `ltr`,
 * направление листа возвращается внутри колонок. Колонки по 347 px, промежуток 24 px
 * (`COLUMN_GAP`, в CSS — `.wpl-main`).
 */
const Pointer: React.FC<{ nos: number[] }> = ({ nos }) => (
  <div className="wpl-pointer" dir="ltr" aria-label={`Main set ${nos.join(', ')} — right column`}>
    {nos.map((n) => <PrintSetNo key={n} n={n} />)}
    <PrintArrow dir="right" stretch className="wpl-pointer-arrow" />
  </div>
);

export const LargeMainLayout: React.FC<{ model: SheetModel; layout: MainLayoutModel }> = ({ model, layout }) => (
  <div className="wpl-main" dir="ltr">
    <div className="wpl-col" dir={model.dir}>
      {layout.left.map((item) =>
        item.kind === 'cube' ? (
          <PrintSetCube key={item.cube.no} cube={item.cube} dir={model.dir} cellChars={model.cellChars} />
        ) : (
          <Pointer key={`p${item.nos[0]}`} nos={item.nos} />
        ),
      )}
    </div>
    <div className="wpl-col" dir={model.dir}>
      {layout.right.map((cube) => (
        <PrintSetCube key={cube.no} cube={cube} dir={model.dir} cellChars={model.cellChars} />
      ))}
    </div>
  </div>
);
