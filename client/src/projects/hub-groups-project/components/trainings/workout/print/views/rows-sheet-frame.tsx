import React from 'react';
import { lineDir } from '../../workout-format';
import { PrintArrow } from '../parts/print-arrow';
import { PrintSetNo } from '../parts/print-set-no';
import type { SheetModel } from '../print-types';
import { FIT_AREA_ATTR, FIT_BOX_ATTR } from '../sheet-fit-measure';
import { setRuled, type RowsPage } from './rows-pages';
import { RowsSet } from './rows-set';

const NO_BELOW: ReadonlySet<string> = new Set();

/**
 * Один лист «листа строк» (хендофф 27.11 §3–§4): шапка (группа · ② уровень · км · метка «1 / 2»),
 * заметка тренера, сеты сверху вниз, на лице `two-sides` — полоса «Next ④ Main set … page 2 →».
 * Тот же компонент рисует видимый лист и скрытую копию замера (`probe`): разметка обязана совпадать,
 * иначе подгонка нечестная. В копии сеты не растягиваются на весь лист — замер видит их высоту.
 *
 * `size` — основной шрифт, px (у копии его ставит замер); без `size` и не копия — пустой белый
 * лист (ещё не подогнан). `below` — строки с текстом под дистанцией при этом размере.
 */
export const RowsSheetFrame: React.FC<{
  model: SheetModel;
  page: RowsPage;
  probe?: boolean;
  size?: number;
  below?: ReadonlySet<string>;
  overflow?: boolean;
}> = ({ model, page, probe = false, size, below = NO_BELOW, overflow = false }) => {
  const { header } = model;
  return (
    <div className="wprint-sheet wprw" dir={model.dir} data-orientation="portrait" {...(probe ? { 'data-probe': '' } : {})}>
      <div className="wprw-area" {...{ [FIT_AREA_ATTR]: '' }}>
        {(probe || size != null) && (
          <div
            className={`wprw-fit${overflow ? ' wprw-floor' : ''}`}
            style={size != null ? { fontSize: `${size}px` } : undefined}
            {...{ [FIT_BOX_ATTR]: '' }}
          >
            <div className="wprw-head">
              <bdi dir={lineDir(header.groupName)} className="wprw-group">{header.groupName}</bdi>
              {header.level && (
                <span className="wprw-level" dir="ltr">
                  <PrintSetNo n={header.level.rank} className="wprw-rank" />
                  <span>{header.level.name}</span>
                </span>
              )}
              <bdi dir="ltr" className="wprw-km">{header.kmLabel}</bdi>
              {page.pageMark && <bdi dir="ltr" className="wprw-page">{page.pageMark}</bdi>}
            </div>
            {page.note && <div className="wprw-note" dir={lineDir(page.note)}>{page.note}</div>}
            <div className="wprw-body">
              {page.cubes.map((cube, i) => (
                <RowsSet key={cube.no} cube={cube} ruled={setRuled(page.cubes, i)} below={below} />
              ))}
            </div>
            {page.nextMainNo != null && (
              // Английский UI — всегда LTR, в любом направлении листа.
              <div className="wprw-foot" dir="ltr">
                <span>Next</span>
                <PrintSetNo n={page.nextMainNo} className="wprw-num" />
                <span>Main set</span>
                <span className="wprw-foot-page">page 2</span>
                <PrintArrow dir="right" />
              </div>
            )}
          </div>
        )}
      </div>
      {overflow && (
        <div className="wprint-noprint wprw-warn" role="alert">
          Too long for one page — the bottom is cut off
        </div>
      )}
    </div>
  );
};
