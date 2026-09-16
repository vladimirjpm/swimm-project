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
  NZL: 'nz', IRL: 'ie', ARM: 'am',
  // Латинская Америка (Маккабиада)
  URU: 'uy', CHI: 'cl', VEN: 've', PER: 'pe', COL: 'co', CRC: 'cr', PAN: 'pa',
  GUA: 'gt', ECU: 'ec', BOL: 'bo', PAR: 'py', CUB: 'cu', DOM: 'do', PUR: 'pr',
  // Европа (остальные из справочника Countries)
  ISL: 'is', MKD: 'mk', MNE: 'me', ALB: 'al', BIH: 'ba', LUX: 'lu', MLT: 'mt',
  MON: 'mc', AND: 'ad',
  // Африка, Азия, Ближний Восток
  ZIM: 'zw', EGY: 'eg', MAR: 'ma', TUN: 'tn', ALG: 'dz', KEN: 'ke', NGR: 'ng',
  ETH: 'et', HKG: 'hk', SGP: 'sg', THA: 'th', PHI: 'ph', INA: 'id', MAS: 'my',
  VIE: 'vn', UAE: 'ae', JOR: 'jo', LBN: 'lb', IRN: 'ir', IRQ: 'iq', SAU: 'sa',
  QAT: 'qa', KUW: 'kw', BRN: 'bh', OMA: 'om',

  // ── Добавлено 16.09.2026 под страницу /records: после боевого прогона в справочнике
  //    рекорды 212 стран, и без этих строк у половины рейтинга флага не было бы вовсе.
  //    ⚠ Коды World Aquatics — не ISO: NIG это Нигер (ne), а Нигерия — NGR (ng); GUI
  //    Гвинея (gn), GBS Гвинея-Бисау (gw), GEQ Экваториальная Гвинея (gq); CGO Конго (cg),
  //    COD ДР Конго (cd). Обрезать код до двух букв нельзя ни в одном из этих случаев.
  AFG: 'af', AGU: 'ai', ANG: 'ao', ANT: 'ag', ARU: 'aw', ASA: 'as', BAH: 'bs',
  BAN: 'bd', BAR: 'bb', BDI: 'bi', BEN: 'bj', BER: 'bm', BHU: 'bt', BIZ: 'bz',
  BOT: 'bw', BRU: 'bn', BUR: 'bf', CAF: 'cf', CAM: 'kh', CAY: 'ky', CGO: 'cg',
  CIV: 'ci', CMR: 'cm', COD: 'cd', COK: 'ck', COM: 'km', CPV: 'cv', CUR: 'cw',
  DJI: 'dj', DMA: 'dm', ERI: 'er', ESA: 'sv', FIJ: 'fj', FRO: 'fo', FSM: 'fm',
  GAB: 'ga', GAM: 'gm', GBS: 'gw', GEQ: 'gq', GHA: 'gh', GIB: 'gi', GRN: 'gd',
  GUI: 'gn', GUM: 'gu', GUY: 'gy', HAI: 'ht', HON: 'hn', ISV: 'vi', IVB: 'vg',
  JAM: 'jm', KGZ: 'kg', LAO: 'la', LBA: 'ly', LBR: 'lr', LCA: 'lc', LES: 'ls',
  LIE: 'li', MAC: 'mo', MAD: 'mg', MAW: 'mw', MDV: 'mv', MGL: 'mn', MHL: 'mh',
  MLI: 'ml', MOZ: 'mz', MRI: 'mu', MTN: 'mr', MYA: 'mm', NAM: 'na', NCA: 'ni',
  NEP: 'np', NIG: 'ne', NMA: 'mp', PAK: 'pk', PLE: 'ps', PLW: 'pw', PNG: 'pg',
  PRK: 'kp', RWA: 'rw', SAM: 'ws', SEN: 'sn', SEY: 'sc', SHN: 'sh', SKN: 'kn',
  SLE: 'sl', SMR: 'sm', SOL: 'sb', SOM: 'so', SRI: 'lk', STP: 'st', SUD: 'sd',
  SUR: 'sr', SWZ: 'sz', SYR: 'sy', TAN: 'tz', TCN: 'tc', TGA: 'to', TJK: 'tj',
  TKM: 'tm', TLS: 'tl', TOG: 'tg', TPE: 'tw', TTO: 'tt', TUV: 'tv', UGA: 'ug',
  VAN: 'vu', VIN: 'vc', YEM: 'ye', ZAM: 'zm',

  // Второй код той же страны у World Aquatics (в справочнике встречаются оба написания).
  IRI: 'ir',   // рядом с IRN
  KSA: 'sa',   // рядом с SAU

  // Территории без собственного ISO-кода — флаг метрополии или спецкод flagcdn.
  MAA: 'sx',   // Синт-Мартен
  TAH: 'pf',   // Таити → Французская Полинезия: своего кода у него нет
  KOS: 'xk',   // Косово: 'xk' — временный код, flagcdn его понимает
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
