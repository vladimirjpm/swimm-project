import React, { useState } from 'react';
import { maxUpdatedAtLabel } from '../../../utils/helpers/records-helper';
import { HOME_REGION, HOME_REGION_LABEL } from '../../../utils/constants/home-region';
import useMode from '../../../hooks/useMode';
import './popup-content-normative.css';
import { type Gender, type PoolType, strokeLabel, normToSeconds, fmt } from './normative/normative-model';
import {
  CAT_LIGHT, CAT_DARK, UI_LIGHT, UI_DARK, STROKES, strokeIconUrl, medStyle,
} from './normative/normative-tokens';
import { useIsMobile } from './normative/use-is-mobile';
import { useNormativeTable } from './normative/use-normative-table';
import { useStyleDropdown } from './normative/use-style-dropdown';
import { useLadderPager } from './normative/use-ladder-pager';

// Попап «Normative Info» (design_handoff_normative_info): матрица нормативов на десктопе,
// пара «текущий → следующий уровень» на мобильном. Логика — в normative/: модель и токены,
// таблица (use-normative-table), пейджер уровней, дропдаун стиля, брейкпоинт.

const PopupContentNormative: React.FC = () => {
  const { mode } = useMode();
  const dark = mode === 'dark';
  const C = dark ? CAT_DARK : CAT_LIGHT;
  const U = dark ? UI_DARK : UI_LIGHT;
  const isMobile = useIsMobile();

  const {
    levelName, isMastersBool,
    poolType, setPoolType,
    stroke, setStroke,
    selectedAgeGroup, setSelectedAgeGroup,
    setDist,
    availableAgeGroups,
    maleStyle, femaleStyle,
    allDistanceKeys, effDist,
    ladder, myIdx,
    useMastersBandRecords, fmtRecord, recordsUpdatedLabel,
  } = useNormativeTable();
  // UI-состояние редизайна
  const { styleOpen, setStyleOpen, ddRef } = useStyleDropdown();
  const [othersOpen, setOthersOpen] = useState(false);
  const { navDir, pIdx, leftLevel, rightLevel, canNext, canPrev, goPrev, goNext } = useLadderPager(ladder, myIdx);

  const menPillStyle: React.CSSProperties = {
    borderRadius: 8, padding: '5px 4px', textAlign: 'center',
    fontSize: 13, fontWeight: 700, fontVariantNumeric: 'tabular-nums',
    background: U.menPill, color: U.menText,
  };
  const womenPillStyle: React.CSSProperties = { ...menPillStyle, background: U.womenPill, color: U.womenText };

  // ==================== Рендер ====================

  const adultAccent = C.adult.accent;

  return (
    <div className="max-h-[75vh] overflow-auto">

      {/* ===== Заголовок (✕ рисует контейнер попапа) ===== */}
      <div className="flex items-center gap-2.5 pr-8">
        <div
          className="flex items-center justify-center"
          style={{ width: 34, height: 34, borderRadius: 10, background: U.subtle, fontSize: 18 }}
        >
          📊
        </div>
        <div>
          <div style={{ fontSize: 20, fontWeight: 900, color: U.text, letterSpacing: '-0.5px', lineHeight: 1 }}>
            Normative Info
          </div>
          <div style={{ fontSize: 12, fontWeight: 600, color: U.label, marginTop: 3 }}>
            {strokeLabel[stroke]}
            {isMastersBool && selectedAgeGroup ? ` · ${selectedAgeGroup}` : ''}
          </div>
        </div>
      </div>

      {/* ===== Контролы: бассейн + Swimming Style (стиль · дистанция) ===== */}
      <div className="mt-3.5 flex flex-wrap items-center gap-4">
        <div style={{ display: 'inline-flex', background: U.subtle, borderRadius: 11, padding: 3 }}>
          {(['25m_pool', '50m_pool'] as PoolType[]).map((pt) => {
            const active = poolType === pt;
            return (
              <button
                key={pt}
                onClick={() => setPoolType(pt)}
                aria-pressed={active}
                style={{
                  fontSize: 12, fontWeight: 700, padding: '6px 13px', borderRadius: 8,
                  cursor: 'pointer', whiteSpace: 'nowrap', border: 'none', transition: 'all .18s ease',
                  background: active ? adultAccent : 'transparent',
                  color: active ? '#fff' : U.label,
                }}
              >
                {pt === '25m_pool' ? '25m' : '50m'}
              </button>
            );
          })}
        </div>

        <div ref={ddRef} style={{ position: 'relative' }}>
          <button
            onClick={() => setStyleOpen((o) => !o)}
            aria-expanded={styleOpen}
            style={{
              display: 'inline-flex', alignItems: 'center', gap: 12, cursor: 'pointer',
              padding: '11px 18px', borderRadius: 13, background: U.subtle, border: 'none', userSelect: 'none',
            }}
          >
            <span style={{ fontSize: 16, fontWeight: 800, color: U.text }}>Swimming Style</span>
            <span
              style={{
                display: 'inline-block', width: 72, height: 36,
                backgroundImage: `url("${strokeIconUrl(stroke)}")`,
                backgroundRepeat: 'no-repeat', backgroundPosition: 'center', backgroundSize: 'contain',
              }}
            />
            <span style={{ fontSize: 16, fontWeight: 800, color: adultAccent }}>· {effDist}</span>
            <span
              style={{
                fontSize: 12, color: adultAccent,
                transform: styleOpen ? 'rotate(0deg)' : 'rotate(180deg)', transition: 'transform .2s ease',
              }}
            >
              ▲
            </span>
          </button>

          {styleOpen && (
            <div
              style={{
                position: 'absolute', top: 'calc(100% + 8px)', left: 0, zIndex: 30,
                background: U.card, border: `1px solid ${U.cardBorder}`, borderRadius: 16,
                boxShadow: U.cardShadow, padding: 14, display: 'flex', gap: 14, alignItems: 'flex-start',
              }}
            >
              {/* Стили — колонка слева */}
              <div style={{ display: 'flex', flexDirection: 'column', gap: 8, paddingRight: 14, borderRight: `1px dashed ${U.grid}` }}>
                {STROKES.map(([k, lab]) => {
                  const active = stroke === k;
                  return (
                    <button
                      key={k}
                      title={lab}
                      aria-pressed={active}
                      onClick={() => setStroke(k)}
                      style={{
                        width: 118, height: 52, borderRadius: 13, cursor: 'pointer', transition: 'all .15s ease',
                        backgroundImage: `url("${strokeIconUrl(k)}")`,
                        backgroundRepeat: 'no-repeat', backgroundPosition: 'center', backgroundSize: 'auto 36px',
                        border: `1.5px solid ${active ? adultAccent : U.cardBorder}`,
                        backgroundColor: active ? C.adult.soft : U.subtle,
                        boxShadow: active ? `0 0 0 2px ${adultAccent}33` : undefined,
                      }}
                    >
                      <span className="sr-only">{lab}</span>
                    </button>
                  );
                })}
              </div>
              {/* Дистанции — колонка справа */}
              <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
                {allDistanceKeys.map((dv) => {
                  const active = effDist === dv;
                  return (
                    <button
                      key={dv}
                      aria-pressed={active}
                      onClick={() => { setDist(dv); setOthersOpen(false); }}
                      style={{
                        textAlign: 'center', padding: '9px 18px', borderRadius: 11, cursor: 'pointer',
                        fontSize: 13, fontWeight: 800, transition: 'all .15s ease',
                        border: `1.5px solid ${active ? adultAccent : U.cardBorder}`,
                        background: active ? adultAccent : U.subtle,
                        color: active ? '#fff' : U.text,
                      }}
                    >
                      {dv}
                    </button>
                  );
                })}
              </div>
            </div>
          )}
        </div>
      </div>

      {/* ===== Возрастные группы (masters) ===== */}
      {isMastersBool && availableAgeGroups.length > 0 && (
        <div className="mt-3">
          <div style={{ fontSize: 11, fontWeight: 800, letterSpacing: '.06em', color: U.label, textTransform: 'uppercase', marginBottom: 6 }}>
            Age Group
          </div>
          <div className="flex gap-2 flex-wrap">
            {availableAgeGroups.map((ag) => {
              const active = selectedAgeGroup === ag;
              return (
                <button
                  key={ag}
                  onClick={() => setSelectedAgeGroup(ag)}
                  aria-pressed={active}
                  style={{
                    fontSize: 12, fontWeight: 700, padding: '6px 13px', borderRadius: 9,
                    cursor: 'pointer', transition: 'all .18s ease',
                    border: `1.5px solid ${active ? adultAccent : U.cardBorder}`,
                    background: active ? adultAccent : U.subtle,
                    color: active ? '#fff' : U.text,
                  }}
                >
                  {ag}
                </button>
              );
            })}
          </div>
        </div>
      )}

      {/* ============ DESKTOP: матрица нормативов ============ */}
      {!isMobile && (
        <>
          <div className="overflow-x-auto" style={{ marginTop: 10 }}>
            <table style={{ borderCollapse: 'separate', borderSpacing: 0, width: '100%', minWidth: 900 }}>
              <thead>
                <tr>
                  <th
                    style={{
                      position: 'sticky', left: 0, zIndex: 3, background: 'var(--theme-mode-surface)',
                      textAlign: 'left', padding: '10px 14px', fontSize: 11, fontWeight: 800,
                      letterSpacing: '.06em', color: U.label, textTransform: 'uppercase',
                      borderBottom: `1px solid ${U.grid}`,
                    }}
                  >
                    Distance
                  </th>
                  {(['WR', 'NR'] as const).map((kind) => (
                    <th key={kind} style={{ padding: '8px 8px 10px', borderBottom: `1px solid ${U.grid}`, verticalAlign: 'bottom' }}>
                      <div className="nrm-med" style={medStyle(C.rec)}>{kind === 'WR' ? 'WR' : HOME_REGION}</div>
                      <div style={{ fontSize: 9, fontWeight: 700, color: U.label, marginTop: 4, textAlign: 'center', letterSpacing: '.04em' }}>
                        {kind === 'WR' ? 'World' : HOME_REGION_LABEL}
                        {useMastersBandRecords ? ` · ${selectedAgeGroup}` : ''}
                      </div>
                      {recordsUpdatedLabel && (
                        <div style={{ fontSize: 8, fontWeight: 600, color: U.label, marginTop: 2, textAlign: 'center', opacity: 0.75 }}>
                          updated {recordsUpdatedLabel}
                        </div>
                      )}
                    </th>
                  ))}
                  {ladder.map((lv, i) => {
                    const cc = C[lv.cat];
                    const isYou = i === myIdx;
                    return (
                      <th key={lv.key} title={lv.key} style={{ padding: '8px 6px 10px', borderBottom: `1px solid ${U.grid}`, verticalAlign: 'bottom' }}>
                        {/* Слот бейджа всегда занимает место — медальоны на одной линии */}
                        <div style={{ height: 15, display: 'flex', alignItems: 'center', justifyContent: 'center', marginBottom: 5 }}>
                          {isYou && (
                            <span
                              style={{
                                fontSize: 8, fontWeight: 900, letterSpacing: '.08em', color: cc.accent,
                                background: cc.soft, padding: '2px 6px', borderRadius: 5, border: `1px solid ${cc.border}`,
                              }}
                            >
                              YOU
                            </span>
                          )}
                        </div>
                        <div className="nrm-med" style={medStyle(cc)}>{lv.label}</div>
                      </th>
                    );
                  })}
                </tr>
              </thead>
              <tbody>
                {allDistanceKeys.map((d) => {
                  const isYouRow = d === effDist;
                  return (
                    <tr key={d}>
                      <td
                        style={{
                          position: 'sticky', left: 0, zIndex: 2, padding: '11px 14px',
                          borderBottom: `1px solid ${U.grid}`, color: U.text,
                          background: isYouRow ? U.rowWash : 'var(--theme-mode-surface)',
                          borderLeft: isYouRow ? `3px solid ${adultAccent}` : undefined,
                        }}
                      >
                        <div style={{ fontSize: 15, fontWeight: 800 }}>{d}</div>
                        {isYouRow && (
                          <div style={{ fontSize: 9, fontWeight: 800, letterSpacing: '.05em', color: adultAccent, textTransform: 'uppercase', marginTop: 2 }}>
                            your event
                          </div>
                        )}
                      </td>
                      {(['WR', 'NR'] as const).map((kind) => (
                        <td
                          key={kind}
                          style={{
                            padding: '7px 7px', borderBottom: `1px solid ${U.grid}`,
                            background: isYouRow ? U.rowWashSoft : 'transparent',
                          }}
                        >
                          <div style={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
                            {(['male', 'female'] as Gender[]).map((g) => {
                              const rec = fmtRecord(g, d, kind);
                              const updatedOne = maxUpdatedAtLabel([rec.updatedAt]);
                              const titleParts = [rec.holder, updatedOne ? `updated ${updatedOne}` : null].filter(Boolean);
                              return (
                                <div
                                  key={g}
                                  style={g === 'male' ? menPillStyle : womenPillStyle}
                                  title={titleParts.length ? titleParts.join(' · ') : undefined}
                                >
                                  {rec.text}
                                </div>
                              );
                            })}
                          </div>
                        </td>
                      ))}
                      {ladder.map((lv, i) => {
                        const cc = C[lv.cat];
                        // Единственный светящийся элемент матрицы — пересечение
                        // (дистанция пловца × его текущий уровень)
                        const cross = isYouRow && i === myIdx;
                        const tdStyle: React.CSSProperties = {
                          padding: '7px 6px',
                          borderBottom: `1px solid ${U.grid}`,
                          background: isYouRow ? U.rowWashSoft : undefined,
                        };
                        if (cross) {
                          tdStyle.background = cc.soft;
                          (tdStyle as any)['--gc'] = cc.accent;
                        }
                        return (
                          <td key={lv.key} className={`nrm-cell${cross ? ' nrm-glow-cell' : ''}`} style={tdStyle}>
                            <div style={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
                              <div style={{ ...menPillStyle, ...(cross ? { fontWeight: 900 } : null) }}>
                                {fmt(maleStyle[d]?.[lv.key])}
                              </div>
                              <div style={{ ...womenPillStyle, ...(cross ? { fontWeight: 900 } : null) }}>
                                {fmt(femaleStyle[d]?.[lv.key])}
                              </div>
                            </div>
                          </td>
                        );
                      })}
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>

          {/* Легенда — под таблицей */}
          <div className="flex items-center gap-2 flex-wrap" style={{ padding: '12px 0 4px', fontSize: 12, color: U.label, fontWeight: 600 }}>
            In each cell:
            <span className="inline-flex items-center gap-1.5">
              <span style={{ width: 22, height: 14, borderRadius: 4, background: U.menPill }} /> top = <b style={{ color: U.menText }}>Men</b>
            </span>
            <span className="inline-flex items-center gap-1.5">
              <span style={{ width: 22, height: 14, borderRadius: 4, background: U.womenPill }} /> bottom = <b style={{ color: U.womenText }}>Women</b>
            </span>
          </div>
        </>
      )}

      {/* ============ MOBILE: сравнение текущего и следующего уровня ============ */}
      {isMobile && leftLevel && rightLevel && (
        <div style={{ marginTop: 12 }}>

          {/* Пейджер по лестнице уровней */}
          <div className="flex items-center gap-2.5" style={{ marginBottom: 12 }}>
            <button
              className="nrm-pager-btn"
              onClick={goPrev}
              disabled={!canPrev}
              aria-label="Slower level"
              style={{
                width: 40, height: 40, borderRadius: 11, display: 'flex', alignItems: 'center', justifyContent: 'center',
                fontSize: 22, fontWeight: 700, border: 'none', background: U.subtle,
                color: canPrev ? U.text : U.label, opacity: canPrev ? 1 : 0.4, cursor: canPrev ? 'pointer' : 'default',
              }}
            >
              ‹
            </button>
            <div style={{ flex: 1, textAlign: 'center', fontSize: 11, fontWeight: 800, letterSpacing: '.08em', color: U.label, textTransform: 'uppercase' }}>
              Level → faster
            </div>
            <button
              className="nrm-pager-btn"
              onClick={goNext}
              disabled={!canNext}
              aria-label="Faster level"
              style={{
                width: 40, height: 40, borderRadius: 11, display: 'flex', alignItems: 'center', justifyContent: 'center',
                fontSize: 22, fontWeight: 700, border: 'none', background: U.subtle,
                color: canNext ? U.text : U.label, opacity: canNext ? 1 : 0.4, cursor: canNext ? 'pointer' : 'default',
              }}
            >
              ›
            </button>
          </div>

          {/* Пара карточек: слева текущий (фокус), справа следующий (быстрее).
              key = pIdx → remount → анимация слайда replay-ится */}
          <div
            key={pIdx}
            className={navDir === 'next' ? 'nrm-slide-next' : navDir === 'prev' ? 'nrm-slide-prev' : ''}
            style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 12 }}
          >
            {[
              { level: leftLevel, isNext: false },
              { level: rightLevel, isNext: true },
            ].map(({ level, isNext }) => {
              const cc = C[level.cat];
              const isYou = level.key.toLowerCase() === (levelName || '').toLowerCase();
              const leftSec = normToSeconds(maleStyle[effDist]?.[leftLevel.key]);
              const rightSec = normToSeconds(maleStyle[effDist]?.[rightLevel.key]);
              const leftSecW = normToSeconds(femaleStyle[effDist]?.[leftLevel.key]);
              const rightSecW = normToSeconds(femaleStyle[effDist]?.[rightLevel.key]);
              const deltaMen = Number.isFinite(leftSec) && Number.isFinite(rightSec) ? (leftSec - rightSec).toFixed(2) : null;
              const deltaWomen = Number.isFinite(leftSecW) && Number.isFinite(rightSecW) ? (leftSecW - rightSecW).toFixed(2) : null;
              return (
                <div
                  key={level.key}
                  style={{
                    display: 'flex', flexDirection: 'column', alignItems: 'center',
                    padding: '14px 12px 16px', borderRadius: 16, background: U.card,
                    border: `2px solid ${isNext ? U.cardBorder : cc.accent}`,
                    boxShadow: isNext ? undefined : `0 0 0 1px ${cc.accent}, 0 8px 26px -8px ${cc.accent}`,
                  }}
                >
                  <span
                    style={{
                      alignSelf: 'flex-start', fontSize: 9, fontWeight: 900, letterSpacing: '.07em',
                      padding: '3px 8px', borderRadius: 6, marginBottom: 10,
                      ...(isNext
                        ? { color: U.label, background: U.subtle, border: `1px solid ${U.cardBorder}` }
                        : { color: '#fff', background: cc.accent }),
                    }}
                  >
                    {isNext ? 'NEXT →' : isYou ? 'YOU' : 'CURRENT'}
                  </span>
                  <div
                    className="nrm-med"
                    style={{ ...medStyle(cc), minWidth: 60, height: 44, fontSize: 18, opacity: isNext ? 0.85 : 1 }}
                  >
                    {level.label}
                  </div>
                  <div style={{ width: '100%', marginTop: 12, display: 'flex', flexDirection: 'column', gap: 7 }}>
                    {(['male', 'female'] as Gender[]).map((g) => {
                      const men = g === 'male';
                      const time = fmt((men ? maleStyle : femaleStyle)[effDist]?.[level.key]);
                      const delta = isNext ? (men ? deltaMen : deltaWomen) : null;
                      const color = men ? U.menText : U.womenText;
                      return (
                        <div
                          key={g}
                          style={{
                            display: 'flex', alignItems: 'center', justifyContent: 'space-between',
                            background: men ? U.menPill : U.womenPill, borderRadius: 9, padding: '7px 11px',
                          }}
                        >
                          <span style={{ fontSize: 10, fontWeight: 800, color }}>{men ? '♂' : '♀'}</span>
                          {/* min-height выравнивает времена в обеих карточках при наличии дельты */}
                          <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'flex-end', justifyContent: 'center', lineHeight: 1.02, minHeight: 30 }}>
                            <span style={{ fontSize: 17, fontWeight: 800, color, fontVariantNumeric: 'tabular-nums' }}>{time}</span>
                            {delta && (
                              <span style={{ fontSize: 10, fontWeight: 700, color, opacity: 0.68, fontVariantNumeric: 'tabular-nums' }}>
                                −{delta}
                              </span>
                            )}
                          </div>
                        </div>
                      );
                    })}
                  </div>
                </div>
              );
            })}
          </div>

          {/* Остальные дистанции — коллапс */}
          <button
            onClick={() => setOthersOpen((o) => !o)}
            aria-expanded={othersOpen}
            style={{
              width: '100%', marginTop: 14, display: 'flex', alignItems: 'center', justifyContent: 'space-between',
              cursor: 'pointer', padding: '12px 14px', background: U.subtle, borderRadius: 13, border: 'none',
            }}
          >
            <span style={{ fontSize: 13, fontWeight: 800, color: U.text }}>Other distances</span>
            <span style={{ fontSize: 11, color: U.label, transform: othersOpen ? 'rotate(180deg)' : 'rotate(0deg)', transition: 'transform .2s ease' }}>
              ▼
            </span>
          </button>
          {othersOpen && (
            <div style={{ marginTop: 8, display: 'flex', flexDirection: 'column', gap: 8 }}>
              {allDistanceKeys.filter((d) => d !== effDist).map((d) => (
                <div key={d} style={{ background: U.card, border: `1px solid ${U.cardBorder}`, borderRadius: 12, padding: '10px 12px' }}>
                  <div style={{ fontSize: 13, fontWeight: 800, color: U.text, marginBottom: 7 }}>{d}</div>
                  <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 8 }}>
                    {[leftLevel, rightLevel].map((level, i) => {
                      const cc = C[level.cat];
                      return (
                        <div key={level.key} style={{ border: `1px solid ${cc.border}`, borderRadius: 9, padding: '7px 9px' }}>
                          <div style={{ fontSize: 9, fontWeight: 800, letterSpacing: '.04em', color: cc.accent, textTransform: 'uppercase', marginBottom: 5 }}>
                            {level.label}{i === 1 ? ' →' : ''}
                          </div>
                          <div style={{ display: 'flex', gap: 8 }}>
                            <span style={{ fontSize: 13, fontWeight: 700, color: U.menText, fontVariantNumeric: 'tabular-nums' }}>
                              {fmt(maleStyle[d]?.[level.key])}
                            </span>
                            <span style={{ fontSize: 13, fontWeight: 700, color: U.womenText, fontVariantNumeric: 'tabular-nums' }}>
                              {fmt(femaleStyle[d]?.[level.key])}
                            </span>
                          </div>
                        </div>
                      );
                    })}
                  </div>
                </div>
              ))}
            </div>
          )}
        </div>
      )}
    </div>
  );
};

export default PopupContentNormative;
