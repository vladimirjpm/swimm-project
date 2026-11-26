import React from 'react';
import { lineDir } from '../../workout-format';
import { PrintSetNo } from '../parts/print-set-no';
import type { SheetModel } from '../print-types';
import { FIT_AREA_ATTR, FIT_BOX_ATTR, type SheetVariant } from '../sheet-fit-measure';
import { LargeCubesLayout } from './large-cubes-layout';
import { LargeMainLayout } from './large-main-layout';

/**
 * Лист вида `large` в одном варианте раскладки (§3.2): шапка-полоса (группа · ② уровень · км),
 * заметка тренера, сеты. Тот же компонент рисует и видимый лист, и скрытые копии для замера
 * (`use-sheet-fit.ts`) — разметка обязана совпадать, иначе подгонка нечестная.
 *
 * `size` — основной шрифт, px; у копий замера его нет (шрифт ставит замер). `variant` null — лист
 * ещё не подогнан: пустой белый лист.
 */
export const LargeSheetFrame: React.FC<{
  model: SheetModel;
  variant: SheetVariant | null;
  size?: number;
  overflow?: boolean;
}> = ({ model, variant, size, overflow = false }) => {
  const { header } = model;
  return (
    <div className="wprint-sheet wpl" dir={model.dir} data-orientation="portrait">
      <div className="wpl-area" {...{ [FIT_AREA_ATTR]: '' }}>
        {variant && (
          <div
            className={`wpl-fit${overflow ? ' wpl-floor' : ''}`}
            style={size != null ? { fontSize: `${size}px` } : undefined}
            {...{ [FIT_BOX_ATTR]: '' }}
          >
            <div className="wpl-head">
              <bdi dir={lineDir(header.groupName)} className="wpl-group">{header.groupName}</bdi>
              <span className="wpl-head-end">
                {header.level && (
                  <span className="wpl-level">
                    <PrintSetNo n={header.level.rank} /> <bdi dir="ltr">{header.level.name}</bdi>
                  </span>
                )}
                <bdi dir="ltr" className="wpl-km">{header.kmLabel}</bdi>
              </span>
            </div>
            {model.note && <div className="wpl-note" dir={lineDir(model.note)}>{model.note}</div>}
            {variant.layout === 'main' && model.mainLayout ? (
              <LargeMainLayout model={model} layout={model.mainLayout} />
            ) : (
              <LargeCubesLayout model={model} columns={variant.layout === 'cubes' ? variant.columns : 1} />
            )}
          </div>
        )}
      </div>
      {overflow && (
        <div className="wprint-noprint wpl-warn" role="alert">
          Too long for one page — the bottom is cut off
        </div>
      )}
    </div>
  );
};
