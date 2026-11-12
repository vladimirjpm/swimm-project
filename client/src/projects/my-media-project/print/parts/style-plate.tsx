import React from 'react';
import UI_SwimmStyleIcon from '../../../components/mix/swimm-style-icon/swimm-style-icon';
import { PRINT_COLOR } from '../print-theme';

/** Размеры плиты в раскладке: ширина плиты, сторона иконки, кегль дистанции. */
export interface StylePlateSpec {
  width: number;
  icon: number;
  len: number;
  radius: number;
  padding: string;
}

/** Светлая плита со стилем и дистанцией: на тёмной панели тёмная иконка без неё не видна. */
export default function StylePlate({ styleName, distanceLabel, spec }: { styleName: string; distanceLabel: string; spec: StylePlateSpec }) {
  return (
    <div
      style={{
        width: spec.width, flex: 'none', boxSizing: 'border-box', padding: spec.padding, borderRadius: spec.radius,
        background: PRINT_COLOR.plate, display: 'flex', flexDirection: 'column', alignItems: 'center',
      }}
    >
      <UI_SwimmStyleIcon
        styleName={styleName}
        styleLen={distanceLabel}
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
