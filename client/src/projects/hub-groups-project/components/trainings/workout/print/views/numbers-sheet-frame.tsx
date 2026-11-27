import React from 'react';
import { lineDir } from '../../workout-format';
import { PrintArrow } from '../parts/print-arrow';
import { PrintSetNo } from '../parts/print-set-no';
import type { SheetModel } from '../print-types';
import { FIT_AREA_ATTR, FIT_BOX_ATTR } from '../sheet-fit-measure';
import type { NumbersPage, NumbersViewKind } from './numbers-pages';
import { NumbersSet } from './numbers-set';

const NO_BELOW: ReadonlySet<string> = new Set();

/**
 * Один лист «листа кубов» (хендофф 27.11 вторая версия, §3, §6): шапка (группа · ② уровень · км ·
 * метка «1 / 2»), заметка тренера, кубы сверху вниз, на лице двустороннего — полоса «Next ④ Main set …
 * page 2 →», последней мелкой строкой — подпись «R = מנוחה · @ = יציאה».
 * Тот же компонент рисует видимый лист и скрытую копию замера (`probe`): разметка обязана совпадать,
 * иначе подгонка нечестная. В копии кубы не растягиваются на весь лист — замер видит их высоту.
 *
 * `size` — основной шрифт, px (у копии его ставит замер); без `size` и не копия — пустой белый
 * лист (ещё не подогнан). `below` — строки с текстом под дистанцией при этом размере.
 */
export const NumbersSheetFrame: React.FC<{
  model: SheetModel;
  page: NumbersPage;
  kind: NumbersViewKind;
  probe?: boolean;
  size?: number;
  below?: ReadonlySet<string>;
  overflow?: boolean;
}> = ({ model, page, kind, probe = false, size, below = NO_BELOW, overflow = false }) => {
  const { header } = model;
  return (
    <div
      className={`wprint-sheet wprn${kind === 'numbers-two-sides' ? ' is-two-sides' : ''}`}
      dir={model.dir}
      data-orientation="portrait"
      {...(probe ? { 'data-probe': '' } : {})}
    >
      <div className="wprn-area" {...{ [FIT_AREA_ATTR]: '' }}>
        {(probe || size != null) && (
          <div
            className={`wprn-fit${overflow ? ' wprn-floor' : ''}`}
            style={size != null ? { fontSize: `${size}px` } : undefined}
            {...{ [FIT_BOX_ATTR]: '' }}
          >
            <div className="wprn-head">
              <bdi dir={lineDir(header.groupName)} className="wprn-group">{header.groupName}</bdi>
              {header.level && (
                <span className="wprn-level" dir="ltr">
                  <PrintSetNo n={header.level.rank} className="wprn-rank" />
                  <span>{header.level.name}</span>
                </span>
              )}
              <bdi dir="ltr" className="wprn-km">{header.kmLabel}</bdi>
              {page.pageMark && <bdi dir="ltr" className="wprn-page">{page.pageMark}</bdi>}
            </div>
            {page.note && <div className="wprn-note" dir={lineDir(page.note)}>{page.note}</div>}
            <div className="wprn-body">
              {page.cubes.map((cube) => <NumbersSet key={cube.no} cube={cube} below={below} />)}
            </div>
            {page.nextMainNo != null && (
              // Английский UI — всегда LTR, в любом направлении листа.
              <div className="wprn-foot" dir="ltr">
                <span>Next</span>
                <PrintSetNo n={page.nextMainNo} className="wprn-num" />
                <span>Main set</span>
                <span className="wprn-foot-page">page 2</span>
                <PrintArrow dir="right" />
              </div>
            )}
            {page.legend.length > 0 && (
              <div className="wprn-legend">
                {page.legend.map((part, i) => (
                  <React.Fragment key={part.sign}>
                    {i > 0 && <span className="wprn-legend-sep">·</span>}
                    {/* «=» — нейтральный знак по направлению листа: в иврите «R» справа, слово слева. */}
                    <bdi dir="ltr" className="wprn-legend-sign">{part.sign}</bdi> ={' '}
                    <bdi dir={lineDir(part.word)}>{part.word}</bdi>
                  </React.Fragment>
                ))}
              </div>
            )}
          </div>
        )}
      </div>
      {overflow && (
        <div className="wprint-noprint wprn-warn" role="alert">
          Too long for one page — the bottom is cut off
        </div>
      )}
    </div>
  );
};
