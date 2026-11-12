import React, { useLayoutEffect, useRef } from 'react';
import CompetitionTile from '../../results-main-project/components/competition-header/competition-tile';
import type { CompetitionTileData } from '../../../utils/helpers/competition-source';
import UI_SwimmStyleIcon from '../../components/mix/swimm-style-icon/swimm-style-icon';
import UI_MedalIcon from '../../components/mix/medal-icon/medal-icon';
import UI_ClubLogo from '../../components/mix/club-logo/club-logo';
import { COVER_FONT } from './cover-fonts';
import './cover.css';

// Три раскладки обложки YouTube (хендофф `!design_handoff/my-media-handoff-nov2026`, задача 2):
// A — `CoverB.dc.html`, B — `CoverB2.dc.html`, C — `CoverG.dc.html`. Размеры, кегли и цвета —
// из inline-стилей макетов; обложка рисуется в ФИКСИРОВАННОМ размере 1280×720, предпросмотр
// уменьшает её transform-ом, а экспорт снимает как есть (`cover-export.ts`).
//
// Цвета здесь — хексы макета, а не роли `--t-*`, и это осознанно: обложка — картинка, а не
// экран сайта. Она обязана выйти одинаковой в светлой и тёмной теме, у любого пользователя.
// По той же причине готовым компонентам (`CompetitionTile`, `UI_MedalIcon`, `UI_ClubLogo`)
// их токены темы заданы на корне обложки постоянными значениями (`ROOT_TOKENS`).

export const COVER_W = 1280;
export const COVER_H = 720;

export type CoverLayout = 'A' | 'B' | 'C';

/** Всё, что обложка печатает. Собирает вызывающий (`swim-list.tsx`) теми же хелперами, что строку. */
export interface CoverData {
  competitionName: string;
  tile: CompetitionTileData;
  /** Значок серии турнира (`MySwimDto.emblem_url`); null — значка нет. */
  emblemUrl: string | null;
  isChampionship: boolean;
  /** День заплыва: `5 NOV`. */
  dayLabel: string;
  /** Дни соревнования: `5–6 NOV`; у однодневного совпадает с `dayLabel`. */
  rangeLabel: string;
  year: string;
  /** Место, как в протоколе; null — места нет (DNS / DQ). */
  place: number | null;
  /** Медаль вручали (`HelperResults.isMedalPlace`) — место ≠ медаль. */
  medal: boolean;
  styleName: string;
  /** `100m`; у эстафеты — как в протоколе (`4x50`). */
  distanceLabel: string;
  /** Время, как в протоколе, либо пометка DNS / DQ. */
  timeText: string;
  /** Одна метка достижения (рекорд > SB > PB) или null. У строки `Under review` (Р76) — null. */
  badge: string | null;
  swimmerName: string;
  clubName: string | null;
}

export interface CoverOptions {
  /** Строка «логотип клуба и имя пловца» (B и C; у A имя есть всегда). */
  showName: boolean;
  /** Место под фото включено (B и C). */
  photoSlot: boolean;
  /** Фото пловца — data-URL выбранного файла; null — не выбрано. */
  photo: string | null;
}

const NAVY = '#0b1b3f';
const INK = '#12305a';
const PANEL_BG = 'rgba(11, 27, 63, 0.72)';
const PLATE_BG = '#e6f2fb';
const BADGE_BG = '#cbd5e1';

/** Токены готовых компонентов — постоянные: обложка не зависит от темы страницы. */
const ROOT_TOKENS = {
  '--theme-comp-tile-bg': '#d3e3ff',
  '--theme-comp-tile-border': '#8fb4ee',
  '--theme-comp-tile-letter': '#0048b8',
  '--theme-comp-tile-strip-bg': '#ffffff',
  '--theme-comp-tile-age-bg': '#ffffff',
  '--theme-comp-tile-age-text': '#00265e',
  // `UI_MedalIcon` без медали печатает номер цветом текста поверхности — здесь она тёмная.
  '--theme-mode-text': '#ffffff',
  // `UI_ClubLogo`: белый круг под логотипом, инициалы тёмным.
  '--theme-mode-border': 'rgba(255, 255, 255, 0.55)',
  '--theme-mode-surface-alt': '#ffffff',
  '--theme-mode-text-secondary': INK,
} as React.CSSProperties;

const base = import.meta.env.BASE_URL;
const POOL_BG = `${base}images/covers/pool-background.jpg`;

/* ── Общие части ─────────────────────────────────────────────────────────── */

/**
 * Строка, которая обязана поместиться: кегль уменьшается от `max` до `min`, пока текст не
 * влезет в отведённую ширину; если не влез и на `min` — при `wrap` переносится на вторую
 * строку. В макетах стоят короткие образцы («אליפות אילת הפתוחה», `1:37.05`, `AGE RECORD`),
 * а настоящие названия, время на 1500 м и `NATIONAL RECORD` длиннее.
 *
 * Кегль ставится в DOM напрямую, а не состоянием: это замер вёрстки, лишний рендер ему не
 * нужен. Обложка появляется только после загрузки шрифтов (`ensureCoverFonts`), поэтому замер
 * идёт настоящим шрифтом.
 */
function FitText({ text, max, min, wrap = false, dir, className, style }: {
  text: string;
  max: number;
  min: number;
  wrap?: boolean;
  dir?: 'rtl' | 'ltr' | 'auto';
  className?: string;
  style?: React.CSSProperties;
}) {
  const ref = useRef<HTMLSpanElement>(null);
  useLayoutEffect(() => {
    const el = ref.current;
    if (!el) return;
    let size = max;
    el.style.whiteSpace = 'nowrap';
    el.style.fontSize = `${size}px`;
    while (size > min && el.scrollWidth > el.clientWidth + 1) {
      size -= 1;
      el.style.fontSize = `${size}px`;
    }
    if (wrap && el.scrollWidth > el.clientWidth + 1) el.style.whiteSpace = 'normal';
  });
  return (
    <span
      ref={ref}
      dir={dir}
      className={className}
      style={{ display: 'block', maxWidth: '100%', overflow: 'hidden', ...style }}
    >
      {text}
    </span>
  );
}

function Background({ dim = false }: { dim?: boolean }) {
  const fill: React.CSSProperties = { position: 'absolute', zIndex: -1, left: 0, top: 0, width: COVER_W, height: COVER_H };
  return (
    <>
      <img src={POOL_BG} alt="" style={{ ...fill, objectFit: 'cover', objectPosition: 'center 62%' }} />
      {dim && <div aria-hidden="true" style={{ ...fill, background: 'rgba(11, 27, 63, 0.4)' }} />}
    </>
  );
}

/** Кубок чемпионата — рисунок макета (на сайте кубок эмодзи, а эмодзи у каждой системы свой). */
function CupIcon({ size }: { size: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 48 48" role="img" aria-label="Championship">
      <path d="M14 6h20v12a10 10 0 0 1-20 0z" fill="#f2b632" />
      <path d="M14 9H7v4a7 7 0 0 0 7 7M34 9h7v4a7 7 0 0 1-7 7" fill="none" stroke="#f2b632" strokeWidth="3" />
      <path d="M21 28h6v8h-6z" fill="#e09a1a" />
      <path d="M15 36h18v5H15z" fill="#f2b632" />
    </svg>
  );
}

/**
 * Что стоит на месте значка соревнования. Правило Влада (12.11.2026): есть значок серии —
 * значок; нет значка, но чемпионат — кубок; иначе пусто, а МЕСТО ОСТАЁТСЯ (раскладка не
 * перестраивается — значок может появиться позже).
 */
function EmblemMark({ d, size, cup }: { d: CoverData; size: number; cup: number }) {
  if (d.emblemUrl) {
    return <img src={d.emblemUrl} alt="" style={{ width: size, height: size, objectFit: 'contain' }} />;
  }
  return d.isChampionship ? <CupIcon size={cup} /> : null;
}

/** Скруглённый угол справа сверху (A и B). */
function Corner({ d }: { d: CoverData }) {
  return (
    <div
      style={{
        position: 'absolute', right: 0, top: 0, width: 180, height: 204, borderRadius: '0 0 0 104px',
        background: '#2b5d8c', display: 'flex', alignItems: 'center', justifyContent: 'center',
      }}
    >
      <EmblemMark d={d} size={156} cup={116} />
    </div>
  );
}

/** Верх A и B: плитка слева, название и плашка-календарь по центру. */
function TopBlock({ d }: { d: CoverData }) {
  return (
    <div style={{ flex: 'none', display: 'grid', gridTemplateColumns: '1fr auto 1fr', alignItems: 'start', gap: 22 }}>
      <CompetitionTile {...d.tile} size="cover" />
      <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 10, maxWidth: 860 }}>
        <FitText
          text={d.competitionName}
          dir="rtl"
          max={66}
          min={40}
          wrap
          style={{ textAlign: 'center', fontFamily: COVER_FONT.hebrew, fontWeight: 900, lineHeight: 1.1 }}
        />
        <div style={{ flex: 'none', borderRadius: 28, overflow: 'hidden', background: '#ffffff', display: 'flex', flexDirection: 'column', textAlign: 'center' }}>
          <span style={{ padding: '12px 40px 10px', background: '#ff7a1a', fontSize: 44, fontWeight: 800, letterSpacing: '0.08em', lineHeight: 1, color: INK, whiteSpace: 'nowrap' }}>
            {d.dayLabel}
          </span>
          <span style={{ padding: '10px 40px 14px', fontFamily: COVER_FONT.digits, fontSize: 84, fontWeight: 900, lineHeight: 1, color: INK }}>
            {d.year}
          </span>
        </div>
      </div>
      <span />
    </div>
  );
}

/**
 * Место кружком — `UI_MedalIcon` в ступенях обложки. Медальная заливка только там, где медаль
 * вручали; иначе номер без кружка (место ≠ медаль). Места нет — ничего.
 */
function PlaceCircle({ d, size }: { d: CoverData; size: 'medal-80' | 'medal-116' }) {
  if (d.place == null) return null;
  return (
    <UI_MedalIcon
      place={String(d.place)}
      styleType={d.medal ? 'icon-place' : 'icon-noplace'}
      styleSize={size}
    />
  );
}

const RIBBON_MEDAL: Record<number, { disc: string; core: string; label: string }> = {
  1: { disc: '#f2b632', core: '#ffd978', label: 'Gold medal' },
  2: { disc: '#b3bdc8', core: '#e4e9ee', label: 'Silver medal' },
  3: { disc: '#c98a4b', core: '#ebbf93', label: 'Bronze medal' },
};

/** Медаль с лентой (обложка C). Без медали — номер места без заливки, как в строке списка. */
function PlaceRibbon({ d }: { d: CoverData }) {
  if (d.place == null) return null;
  const medal = d.medal ? RIBBON_MEDAL[d.place] : undefined;
  if (!medal) return <PlaceCircle d={d} size="medal-80" />;
  return (
    <svg width="40" height="86" viewBox="0 0 28 60" role="img" aria-label={medal.label} style={{ flex: 'none' }}>
      <path d="M9 0h10v30H9z" fill="#e2452b" />
      <circle cx="14" cy="44" r="13" fill={medal.disc} />
      <circle cx="14" cy="44" r="8" fill={medal.core} />
    </svg>
  );
}

/** Светлая плита со стилем и дистанцией: на тёмной панели тёмная иконка без неё не видна. */
function StylePlate({ d, width, icon, len, radius, padding }: {
  d: CoverData; width: number; icon: number; len: number; radius: number; padding: string;
}) {
  return (
    <div
      style={{
        width, flex: 'none', boxSizing: 'border-box', padding, borderRadius: radius, background: PLATE_BG,
        display: 'flex', flexDirection: 'column', alignItems: 'center',
      }}
    >
      <UI_SwimmStyleIcon
        styleName={d.styleName}
        styleLen={d.distanceLabel}
        styleType="icon-len"
        lenPlacement="below"
        iconSize={icon}
        lenSize={len}
        surface="light"
        className="src-cover-layouts"
      />
    </div>
  );
}

/** Время — Montserrat 900 italic с тёмной обводкой (`.ytc-time`); кегль подгоняется по ширине. */
function TimeText({ d, max, min, stroke }: { d: CoverData; max: number; min: number; stroke: number }) {
  return (
    <FitText
      text={d.timeText}
      max={max}
      min={min}
      className="ytc-time"
      // Поля — под наклон курсива и обводку: `overflow: hidden` подгонки иначе срезал бы края.
      style={{
        fontFamily: COVER_FONT.digits, fontStyle: 'italic', fontWeight: 900, lineHeight: 1,
        padding: '0 10px 2px 4px', margin: '0 -10px -2px -4px', boxSizing: 'content-box',
        WebkitTextStroke: `${stroke}px ${NAVY}`,
      }}
    />
  );
}

function Badge({ d, max, min, padding, radius }: { d: CoverData; max: number; min: number; padding: string; radius: number }) {
  if (!d.badge) return null;
  return (
    <FitText
      text={d.badge}
      max={max}
      min={min}
      style={{ boxSizing: 'border-box', padding, borderRadius: radius, background: BADGE_BG, fontWeight: 800, letterSpacing: '0.04em', lineHeight: 1, color: INK }}
    />
  );
}

function NameRow({ d, logo, size }: { d: CoverData; logo: number; size: number }) {
  return (
    <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', gap: 14, maxWidth: '100%' }}>
      {d.clubName && <UI_ClubLogo clubName={d.clubName} size={logo} />}
      <span style={{ minWidth: 0 }}>
        <FitText
          text={d.swimmerName}
          dir="rtl"
          max={size}
          min={Math.round(size * 0.6)}
          style={{ fontFamily: COVER_FONT.hebrew, fontWeight: 900, lineHeight: 1.1 }}
        />
      </span>
    </div>
  );
}

/**
 * Правая колонка B и C: необязательное фото и имя под ним. Рамка «выберите фото» видна только
 * в предпросмотре (`data-cover-skip` — экспорт её пропускает): в файл заглушка не попадает.
 */
function PhotoAndName({ d, o }: { d: CoverData; o: CoverOptions }) {
  return (
    <div style={{ flex: '1 1 0', minWidth: 0, display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'flex-end', gap: 14 }}>
      {o.photoSlot && (o.photo ? (
        <div style={{ flex: '1 1 0', minHeight: 0, alignSelf: 'stretch', borderRadius: 28, overflow: 'hidden' }}>
          <img src={o.photo} alt="" style={{ display: 'block', width: '100%', height: '100%', objectFit: 'cover' }} />
        </div>
      ) : (
        <div
          data-cover-skip="1"
          style={{
            flex: '1 1 0', minHeight: 0, alignSelf: 'stretch', boxSizing: 'border-box', border: '3px dashed #9cc3e8',
            borderRadius: 28, display: 'flex', alignItems: 'center', justifyContent: 'center',
            fontSize: 22, fontWeight: 800, letterSpacing: '0.14em',
          }}
        >
          CHOOSE A PHOTO
        </div>
      ))}
      {o.showName && <NameRow d={d} logo={54} size={44} />}
    </div>
  );
}

const ROOT: React.CSSProperties = {
  ...ROOT_TOKENS,
  position: 'relative', zIndex: 0, overflow: 'hidden', width: COVER_W, height: COVER_H, boxSizing: 'border-box',
  display: 'flex', background: NAVY, fontFamily: COVER_FONT.text, color: '#ffffff', direction: 'ltr',
  textAlign: 'left', lineHeight: 'normal', letterSpacing: 'normal',
};

/* ── Раскладки ───────────────────────────────────────────────────────────── */

/** A: результат крупной строкой, имя с логотипом клуба мелко внизу. Переключателей нет. */
function CoverA({ d }: { d: CoverData }) {
  return (
    <div className="ytc" style={{ ...ROOT, padding: '40px 48px', flexDirection: 'column', gap: 18 }}>
      <Background />
      <Corner d={d} />
      <TopBlock d={d} />
      <div
        style={{
          marginTop: 'auto', alignSelf: 'center', maxWidth: '100%', boxSizing: 'border-box', padding: '18px 40px 18px 26px',
          borderRadius: 40, background: PANEL_BG, display: 'flex', alignItems: 'center', gap: 34,
        }}
      >
        <PlaceCircle d={d} size="medal-116" />
        <StylePlate d={d} width={290} icon={250} len={44} radius={22} padding="10px 0 8px" />
        <div style={{ minWidth: 0, display: 'flex', flexDirection: 'column', alignItems: 'flex-start', gap: 8 }}>
          <TimeText d={d} max={132} min={84} stroke={4} />
          <Badge d={d} max={50} min={30} padding="10px 22px" radius={16} />
        </div>
      </div>
      <NameRow d={d} logo={62} size={54} />
    </div>
  );
}

/** B: панель результата на половину ширины слева внизу, справа фото и имя. */
function CoverB({ d, o }: { d: CoverData; o: CoverOptions }) {
  return (
    <div className="ytc" style={{ ...ROOT, padding: '40px 48px', flexDirection: 'column', gap: 24 }}>
      <Background />
      <Corner d={d} />
      <TopBlock d={d} />
      <div style={{ flex: '1 1 0', minHeight: 0, display: 'flex', alignItems: 'stretch', gap: 40 }}>
        <div
          style={{
            width: 652, flex: 'none', alignSelf: 'flex-end', boxSizing: 'border-box', padding: '18px 22px',
            borderRadius: 36, background: PANEL_BG, display: 'flex', alignItems: 'center', gap: 16,
          }}
        >
          <PlaceCircle d={d} size="medal-80" />
          <StylePlate d={d} width={172} icon={150} len={32} radius={18} padding="8px 0 6px" />
          <div style={{ flex: '1 1 0', minWidth: 0, display: 'flex', flexDirection: 'column', alignItems: 'flex-start', gap: 6 }}>
            <TimeText d={d} max={82} min={48} stroke={3} />
            <Badge d={d} max={40} min={22} padding="8px 14px" radius={12} />
          </div>
        </div>
        <PhotoAndName d={d} o={o} />
      </div>
    </div>
  );
}

/** C: слева тёмная колонка (дата, год, значок, плитка), справа название и результат столбиком. */
function CoverC({ d, o }: { d: CoverData; o: CoverOptions }) {
  return (
    <div className="ytc" style={ROOT}>
      <Background dim />
      <div
        style={{
          width: 300, flex: 'none', boxSizing: 'border-box', padding: '36px 24px', background: 'rgba(11, 27, 63, 0.82)',
          display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'space-between',
        }}
      >
        <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 4, maxWidth: '100%' }}>
          <FitText text={d.rangeLabel} max={26} min={16} style={{ fontWeight: 800, letterSpacing: '0.06em', lineHeight: 1 }} />
          <span style={{ fontFamily: COVER_FONT.digits, fontStyle: 'italic', fontSize: 100, fontWeight: 900, lineHeight: 1, color: '#f5a623' }}>
            {d.year}
          </span>
        </div>
        {/* Место значка держит размер и без значка — колонка не перестраивается. */}
        <div style={{ width: 220, height: 220, flex: 'none', display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
          <EmblemMark d={d} size={220} cup={170} />
        </div>
        <CompetitionTile {...d.tile} size="cover-lg" />
      </div>
      <div style={{ flex: '1 1 0', minWidth: 0, boxSizing: 'border-box', padding: '36px 48px 40px', display: 'flex', flexDirection: 'column', gap: 22 }}>
        <FitText
          text={d.competitionName}
          dir="rtl"
          max={60}
          min={40}
          wrap
          style={{ flex: 'none', alignSelf: 'center', textAlign: 'center', fontFamily: COVER_FONT.hebrew, fontWeight: 900, lineHeight: 1.1 }}
        />
        <div style={{ flex: '1 1 0', minHeight: 0, display: 'flex', alignItems: 'stretch', gap: 40 }}>
          <div
            style={{
              width: 400, flex: 'none', boxSizing: 'border-box', padding: '14px 20px', borderRadius: 36, background: PANEL_BG,
              display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center', gap: 12,
            }}
          >
            <PlaceRibbon d={d} />
            <StylePlate d={d} width={330} icon={280} len={50} radius={22} padding="10px 0 8px" />
            <TimeText d={d} max={92} min={56} stroke={3} />
            <Badge d={d} max={44} min={26} padding="9px 20px" radius={14} />
          </div>
          <PhotoAndName d={d} o={o} />
        </div>
      </div>
    </div>
  );
}

/** Обложка выбранной раскладки, 1280×720. `forwardRef` — экспорт снимает именно этот узел. */
const Cover = React.forwardRef<HTMLDivElement, { layout: CoverLayout; data: CoverData; options: CoverOptions }>(
  ({ layout, data, options }, ref) => (
    <div ref={ref} style={{ width: COVER_W, height: COVER_H }}>
      {layout === 'A' ? <CoverA d={data} /> : layout === 'B' ? <CoverB d={data} o={options} /> : <CoverC d={data} o={options} />}
    </div>
  ),
);
Cover.displayName = 'Cover';

export default Cover;
