import React from 'react';
import './pull-buoy-icon.css';
import { useAppDispatch } from '../../../../store/store';

interface UI_PullBuoyIconProps {
  className?: string;
  iconWidth?: string;
  styleType?: 'icon-notext' | 'icon-text-bottom' | 'icon-text-right';
  label?: string;
}

const UI_PullBuoyIcon: React.FC<UI_PullBuoyIconProps> = ({
  className = '',
  iconWidth = '8',
  styleType = 'icon-notext',
  label = 'Pull Buoy',
}) => {
  const dispatch = useAppDispatch();

  const svg = (
    <svg
      viewBox="0 0 120 180"
      width={iconWidth}
      height={iconWidth}
      xmlns="http://www.w3.org/2000/svg"
      fill="none"
      stroke="currentColor"
      strokeWidth="2"
      strokeLinecap="round"
      strokeLinejoin="round"
      className={`object-contain ${className}`}
    >
      {/* Задний слой (контур буйка) */}
      <path
        d="M20 30
           C 20 15, 40 5, 60 5
           C 80 5, 100 15, 100 30
           C 100 55, 88 70, 78 90
           C 74 96, 74 84, 78 90
           C 88 110, 100 125, 100 150
           C 100 165, 80 175, 60 175
           C 40 175, 20 165, 20 150
           C 20 125, 32 110, 42 90
           C 46 84, 46 96, 42 90
           C 32 70, 20 55, 20 30 Z"
        fill="#1e1e1e"
      />
      {/* Средняя белая полоса */}
      <path
        d="M34 35
           C 34 20, 52 10, 70 10
           C 88 10, 96 20, 96 35
           C 96 50, 84 68, 76 90
           C 74 94, 74 86, 76 90
           C 84 112, 96 130, 96 148
           C 96 162, 88 170, 70 170
           C 52 170, 34 162, 34 148
           C 34 130, 46 112, 54 90
           C 56 86, 56 94, 54 90
           C 46 68, 34 50, 34 35 Z"
        fill="#ffffff"
      />
      {/* Передний слой (тень для объёма) */}
      <path
        d="M50 38
           C 50 25, 65 18, 78 18
           C 90 18, 100 25, 100 38
           C 100 54, 90 72, 84 90
           C 82 94, 82 86, 84 90
           C 90 108, 100 125, 100 145
           C 100 158, 90 168, 78 168
           C 65 168, 50 158, 50 145
           C 50 125, 60 108, 66 90
           C 68 86, 68 94, 66 90
           C 60 72, 50 54, 50 38 Z"
        fill="#1e1e1e"
      />
    </svg>
  );

  if (styleType === 'icon-text-bottom') {
    return (
      <div className="dv-pullbuoy-icon flex flex-col items-center space-y-1 text-gray-800 text-base">
        {svg}
        <span>{label}</span>
      </div>
    );
  }

  if (styleType === 'icon-text-right') {
    return (
      <div className="dv-pullbuoy-icon flex flex-row items-center gap-2 text-gray-800 text-base">
        {svg}
        <span className="w-fit text-2xl">{label}</span>
      </div>
    );
  }

  return (
    <div
      className="dv-pullbuoy-icon w-fit h-auto flex items-center justify-center text-gray-900"
      title={label}
    >
      {svg}
    </div>
  );
};

export default UI_PullBuoyIcon;
