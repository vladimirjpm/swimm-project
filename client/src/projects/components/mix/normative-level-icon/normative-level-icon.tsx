import './normative-level-icon.css';
import { rootActions, useAppDispatch } from '../../../../store/store';
import React from 'react';
import { Enums } from '../../../../utils/interfaces/enums';

interface UI_NormativeLevelIconProps {
  levelName: string;
  styleType: string;//component style
  styleSize: string;
  className?: string;
  styleName?: string;//swimm style name
  styleLen?: string;//swimm style len
  poolType?: string;//swimm pool len
  normativeAgeGroup?: string | null;//masters age group key e.g. "25-29"
  isMasters?: boolean | null;//is masters normative
  disableClick?: boolean;//disable onClick popup
  progressPercent?: number | null;//gauge fill: % to next level
  nextTime?: string | null;//gauge tooltip: target time of next level
  showProgress?: boolean;//gauge: show/hide progress arc (label always shown)
}

// Категория уровня → цвет дуги/метки (youth/adult/pro). Статус-цвета, фиксированы
// в обоих режимах (как медали); трек дуги берёт mode-переменную.
const CAT_COLORS = { youth: '#1b8a4b', adult: '#4f46e5', pro: '#ea580c', none: '#9aa4b2' } as const;
const getLevelCategoryColor = (levelName: string): string => {
  const n = (levelName || '').toLowerCase();
  if (n.includes('youth')) return CAT_COLORS.youth;
  if (n.includes('adult')) return CAT_COLORS.adult;
  if (n === 'kms' || n === 'ms' || n === 'msmk') return CAT_COLORS.pro;
  return CAT_COLORS.none;
};

const getMedalClassAndLabel = (levelName: string): { label: string; className: string } => {
  switch (levelName) {
    case 'I_youth':
      return { label: '1y', className: 'normative-medal gold' };
    case 'II_youth':
      return { label: '2y', className: 'normative-medal silver' };
    case 'III_youth':
      return { label: '3y', className: 'normative-medal bronze' };
    case 'I_adult':
      return { label: 'I', className: 'normative-medal gold' };
    case 'II_adult':
      return { label: 'II', className: 'normative-medal silver' };
    case 'III_adult':
      return { label: 'III', className: 'normative-medal bronze' };
    case 'KMS':
      return { label: 'KMS', className: 'normative-medal bronze' };
    case 'MS':
      return { label: 'MS', className: 'normative-medal silver' };
    case 'MSMK':
      return { label: 'MSMK', className: 'normative-medal gold' };
    default:
      return { label: levelName, className: 'normative-medal' };
  }
};

const UI_NormativeLevelIcon: React.FC<UI_NormativeLevelIconProps> = ({
  levelName,
  styleType = '',
  styleSize = 'size-1',
  className = '',
  styleName, 
  styleLen, 
  poolType,
  normativeAgeGroup,
  isMasters,
  disableClick = false,
  progressPercent,
  nextTime,
  showProgress = true,
}) => {
  const dispatch = useAppDispatch();
  const handleNormativeClick = () => {
    if (disableClick) return;
        dispatch(
      rootActions.updateState({
          isPopup: true,
          popUpType: Enums.PopupType.normative,
          popUpObj: {levelName, styleName, styleLen, poolType, isMasters: isMasters ?? false, normativeAgeGroup}
      })
    );
  };

  if (styleType === 'gauge') {
    const { label } = getMedalClassAndLabel(levelName);
    const color = getLevelCategoryColor(levelName);
    const ARC = 87.96; // длина полудуги r=28 (как в прототипе)
    const pct =
      typeof progressPercent === 'number' && Number.isFinite(progressPercent)
        ? Math.min(100, Math.max(0, progressPercent))
        : null;
    // Заполнение: по проценту; если след. уровня нет (max) — дуга полная.
    const offset = pct !== null ? ARC * (1 - pct / 100) : 0;

    if (!showProgress) {
      return (
        <div
          className={`dv-normative-level-icon flex items-center gap-1 ${disableClick ? '' : 'cursor-pointer'} ${className}`}
          onClick={handleNormativeClick}
        >
          <span className="text-sm font-semibold text-[var(--theme-mode-text-muted)]">Level:</span>
          <span className="text-[15px] font-extrabold" style={{ color }}>
            {label}
          </span>
          {normativeAgeGroup && (
            <span className="normative-age-group font-semibold text-[10px]" style={{ color }}>
              {normativeAgeGroup}
            </span>
          )}
        </div>
      );
    }

    return (
      <div
        className={`dv-normative-level-icon group relative flex flex-col items-center ${disableClick ? '' : 'cursor-pointer'} ${className}`}
        onClick={handleNormativeClick}
      >
        {pct !== null && nextTime && (
          <div className="pointer-events-none absolute bottom-full left-1/2 z-20 mb-1.5 hidden -translate-x-1/2 flex-col items-center gap-0.5 whitespace-nowrap rounded-lg bg-[#1f2733] px-2.5 py-1.5 shadow-lg group-hover:flex">
            <span className="text-[13px] font-extrabold" style={{ color }}>
              {Math.round(pct)}% to next
            </span>
            <span className="text-[11px] font-semibold text-[#c4cbd6] tabular-nums">
              → next: {nextTime}
            </span>
          </div>
        )}
        <div className="relative w-[72px] h-[41px]">
          <svg width="72" height="41" viewBox="0 0 72 41" className="block">
            <path
              d="M 8 38 A 28 28 0 0 1 64 38"
              fill="none"
              stroke="var(--theme-mode-border)"
              strokeWidth="3.5"
              strokeLinecap="round"
            />
            <path
              d="M 8 38 A 28 28 0 0 1 64 38"
              fill="none"
              stroke={color}
              strokeWidth="3.5"
              strokeLinecap="round"
              strokeDasharray={ARC}
              strokeDashoffset={offset}
            />
          </svg>
          <div className="absolute inset-x-0 bottom-px text-center">
            <span className="text-[15px] font-extrabold" style={{ color }}>
              {label}
            </span>
          </div>
        </div>
        {normativeAgeGroup && (
          <div className="normative-age-group font-semibold text-[10px]" style={{ color }}>
            {normativeAgeGroup}
          </div>
        )}
      </div>
    );
  }

  if (styleType === '') {
    return (
      <div
        className={`dv-normative-level-icon text-gray-800 text-base ${className}}`}
        onClick={handleNormativeClick}
      >
        NONE
      </div>
    );
  }

  const { label, className: medalClass } = getMedalClassAndLabel(levelName);

  return (
    <div
      className={`dv-normative-level-icon ${disableClick ? '' : 'cursor-pointer'} ${className}`}
      onClick={handleNormativeClick}
    >
      <div className={`${medalClass} ${styleSize} level-${levelName.toLowerCase()}`}>{label}</div>
      {normativeAgeGroup && (
        <div className="normative-age-group font-semibold">{normativeAgeGroup}</div>
      )}
    </div>
  );
};

export default UI_NormativeLevelIcon;