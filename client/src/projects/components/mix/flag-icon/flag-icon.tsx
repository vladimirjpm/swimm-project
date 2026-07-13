import './flag-icon.css';
import React from 'react';

type FlagSize =
  | '16x12' | '20x15' | '24x18' | '28x21' | '32x24' | '36x27'
  | '40x30' | '48x36' | '56x42' | '60x45' | '64x48' | '72x54'
  | '80x60' | '84x63' | '96x72' | '108x81' | '112x84' | '120x90'
  | '128x96' | '144x108' | '160x120' | '192x144' | '224x168' | '256x192';

type FlagEmojiProps = {
  countryCode: string;
  className?: string;
  title?: string;
  size?: FlagSize;
};

// В данных страны лежат alpha-3 кодами World Aquatics (ISR, GER, NED…), а flagcdn
// понимает только ISO alpha-2 — маппим явно (обрезать до 2 букв нельзя: ISR→"is" = Исландия).
const ALPHA3_TO_ALPHA2: Record<string, string> = {
  ISR: 'il', USA: 'us', GBR: 'gb', GER: 'de', FRA: 'fr', ITA: 'it', ESP: 'es',
  NED: 'nl', HUN: 'hu', AUS: 'au', CAN: 'ca', RUS: 'ru', UKR: 'ua', POL: 'pl',
  CZE: 'cz', SVK: 'sk', AUT: 'at', SUI: 'ch', SWE: 'se', NOR: 'no', DEN: 'dk',
  FIN: 'fi', BEL: 'be', POR: 'pt', GRE: 'gr', TUR: 'tr', ROU: 'ro', BUL: 'bg',
  SRB: 'rs', CRO: 'hr', SLO: 'si', LTU: 'lt', LAT: 'lv', EST: 'ee', GEO: 'ge',
  AZE: 'az', KAZ: 'kz', UZB: 'uz', MDA: 'md', BLR: 'by', CYP: 'cy', JPN: 'jp',
  CHN: 'cn', KOR: 'kr', IND: 'in', RSA: 'za', BRA: 'br', ARG: 'ar', MEX: 'mx',
  NZL: 'nz', IRL: 'ie',
};

/** ISR → il; il/IL → il; неизвестный код → null (флаг не рисуем). */
function toAlpha2(countryCode: string): string | null {
  const norm = countryCode.trim().toUpperCase();
  if (norm.length === 2) return norm.toLowerCase();
  if (norm.length === 3) return ALPHA3_TO_ALPHA2[norm] ?? null;
  return null;
}

const UI_FlagEmoji: React.FC<FlagEmojiProps> = ({
  countryCode,
  className = '',
  title,
  size = '24x18',
}) => {
  const code = countryCode ? toAlpha2(countryCode) : null;
  if (!code) return null;
  const [width, height] = size.split('x').map(Number);
  const src = `https://flagcdn.com/${size}/${code}.png`;

  return (
    <img
      src={src}
      alt={`Flag of ${code.toUpperCase()}`}
      title={title || code.toUpperCase()}
      className={className}
      width={width}
      height={height}
      style={{ objectFit: 'cover', borderRadius: '2px' }}
    />
  );
};

export default UI_FlagEmoji;
