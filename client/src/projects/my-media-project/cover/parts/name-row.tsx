import React from 'react';
import UI_ClubLogo from '../../../components/mix/club-logo/club-logo';
import type { CoverData } from '../cover-data';
import { COVER_FONT } from '../cover-fonts';
import FitText from './fit-text';

export interface NameSpec {
  /** Диаметр логотипа клуба. */
  logo: number;
  /** Кегль имени. */
  size: number;
}

/** Длинное имя ужимается не сильнее этой доли кегля. */
const MIN_SHARE = 0.6;

/** Логотип клуба и имя пловца (иврит, справа налево). */
export default function NameRow({ data, spec }: { data: CoverData; spec: NameSpec }) {
  return (
    <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', gap: 14, maxWidth: '100%' }}>
      {data.clubName && <UI_ClubLogo clubName={data.clubName} size={spec.logo} />}
      <span style={{ minWidth: 0 }}>
        <FitText
          text={data.swimmerName}
          dir="rtl"
          size={{ max: spec.size, min: Math.round(spec.size * MIN_SHARE) }}
          style={{ fontFamily: COVER_FONT.hebrew, fontWeight: 900, lineHeight: 1.1 }}
        />
      </span>
    </div>
  );
}
