import React from 'react';
import UI_SwimmStyleIcon from '../../../components/mix/swimm-style-icon/swimm-style-icon';
import type { CoverData } from '../cover-data';
import { COVER_COLOR } from '../cover-theme';

/** Размеры плиты в раскладке: ширина плиты, сторона иконки, кегль дистанции. */
export interface StylePlateSpec {
  width: number;
  icon: number;
  len: number;
  radius: number;
  padding: string;
}

/** Светлая плита со стилем и дистанцией: на тёмной панели тёмная иконка без неё не видна. */
export default function StylePlate({ data, spec }: { data: CoverData; spec: StylePlateSpec }) {
  return (
    <div
      style={{
        width: spec.width, flex: 'none', boxSizing: 'border-box', padding: spec.padding, borderRadius: spec.radius,
        background: COVER_COLOR.plate, display: 'flex', flexDirection: 'column', alignItems: 'center',
      }}
    >
      <UI_SwimmStyleIcon
        styleName={data.styleName}
        styleLen={data.distanceLabel}
        styleType="icon-len"
        lenPlacement="below"
        iconSize={spec.icon}
        lenSize={spec.len}
        surface="light"
        className="src-style-plate"
      />
    </div>
  );
}
