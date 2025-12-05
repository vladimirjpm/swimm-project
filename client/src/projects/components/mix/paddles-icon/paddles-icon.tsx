import React from 'react';
import './paddles-icon.css';
import { useAppDispatch } from '../../../../store/store';

interface UI_PaddlesIconProps {
  className?: string;
  iconWidth?: string;
  styleType?: 'icon-notext' | 'icon-text-bottom' | 'icon-text-right';
  label?: string;
}

const UI_PaddlesIcon: React.FC<UI_PaddlesIconProps> = ({
  className = '',
  iconWidth = '8',
  styleType = 'icon-notext',
  label = 'Paddles',
}) => {
  const dispatch = useAppDispatch();

  const svg = (
    <svg
      viewBox="0 0 128 128"
      width={iconWidth}
      height={iconWidth}
      fill="none"
      stroke="currentColor"
      strokeWidth="6"
      strokeLinecap="round"
      strokeLinejoin="round"
      className={`object-contain ${className}`}
    >
      {/* Контур лопатки */}
      <path d="M24 82c0-18 4-33 12-45 7-10 18-19 28-19s18 6 25 16c9 12 15 28 15 48 0 13-6 21-17 25-9 3-19 5-30 5-15 0-33-7-33-30z" />
      {/* Палец/крепление сверху */}
      <path d="M64 20c5 8 8 18 8 30" />
      {/* Отверстия под пальцы */}
      <circle cx="50" cy="62" r="6" />
      <circle cx="66" cy="54" r="6" />
      <circle cx="80" cy="66" r="6" />
      {/* Ремешок (перемычка) */}
      <path d="M44 86c10-6 30-6 40 0" />
    </svg>
  );

  if (styleType === 'icon-text-bottom') {
    return (
      <div className="dv-paddles-icon flex flex-col items-center space-y-1 text-gray-800 text-base">
        {svg}
        <span>{label}</span>
      </div>
    );
  }

  if (styleType === 'icon-text-right') {
    return (
      <div className="dv-paddles-icon flex flex-row items-center gap-2 text-gray-800 text-base">
        {svg}
        <span className="w-fit text-2xl">{label}</span>
      </div>
    );
  }

  return (
    <div className="dv-paddles-icon w-fit h-auto flex items-center justify-center text-gray-900" title={label}>
      {svg}
    </div>
  );
};

export default UI_PaddlesIcon;
