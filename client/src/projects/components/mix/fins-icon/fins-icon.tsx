import React from 'react';
import './fins-icon.css';

interface UI_FinsIconProps {
  className?: string;
  iconWidth?: string;
  styleType?: 'icon-notext' | 'icon-text-bottom' | 'icon-text-right';
  label?: string;
}

/**
 * Ласты — в той же манере, что UI_PaddlesIcon: только контур `currentColor` (цвет — из
 * .dv-fins-icon), толщина линии в той же пропорции к viewBox (~4.7%). Форма — пара ласт из
 * макета Влада (lasty.svg, 15.09.2026), заливки сняты, чтобы значок жил в обеих темах.
 */
const UI_FinsIcon: React.FC<UI_FinsIconProps> = ({
  className = '',
  iconWidth = '8',
  styleType = 'icon-notext',
  label = 'Fins',
}) => {
  const svg = (
    <svg
      viewBox="0 0 300 250"
      width={iconWidth}
      height={iconWidth}
      fill="none"
      stroke="currentColor"
      strokeWidth="14"
      strokeLinecap="round"
      strokeLinejoin="round"
      className={`object-contain ${className}`}
    >
      {/* Лопасти — без верхнего края: его закрывает калоша */}
      <path d="M47 69 C43 91 28 119 20 151 C13 180 13 213 16 229 C43 238 82 238 111 229 C114 203 110 172 102 143 L82 69" />
      <path d="M218 69 L198 143 C190 172 186 203 189 229 C218 238 257 238 284 229 C287 213 287 180 280 151 C272 119 257 91 253 69" />
      {/* Калоши */}
      <path d="M43 38 C43 24 53 17 65 17 C77 17 87 24 87 38 V63 C87 80 78 90 65 90 C52 90 43 80 43 63 Z" />
      <path d="M213 38 C213 24 223 17 235 17 C247 17 257 24 257 38 V63 C257 80 248 90 235 90 C222 90 213 80 213 63 Z" />
    </svg>
  );

  if (styleType === 'icon-text-bottom') {
    return (
      <div className="dv-fins-icon flex flex-col items-center space-y-1 text-base">
        {svg}
        <span>{label}</span>
      </div>
    );
  }

  if (styleType === 'icon-text-right') {
    return (
      <div className="dv-fins-icon flex flex-row items-center gap-2 text-base">
        {svg}
        <span className="w-fit text-2xl">{label}</span>
      </div>
    );
  }

  return (
    <div className="dv-fins-icon w-fit h-auto flex items-center justify-center" title={label}>
      {svg}
    </div>
  );
};

export default UI_FinsIcon;
