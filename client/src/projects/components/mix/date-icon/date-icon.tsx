import './date-icon.css';
import { useAppDispatch } from '../../../../store/store';
import React from 'react';

interface UI_DateIconProps {
  styleType?: 'cube'  | 'row-style-1' | 'row-style-2';
  date?: string; // формат: 'DD-MM-YYYY'
  paddingClass?: string;
  className?: string;
  fontClassName?: string; // переопределяет текстовый стиль (size/weight/color) для row-style-1
}

const parseCustomDate = (dateStr?: string): Date => {
  if (!dateStr) return new Date();

  const [day, month, year] = dateStr.split('/').map(Number);
  return new Date(year, month - 1, day);
};

const UI_DateIcon: React.FC<UI_DateIconProps> = ({
  styleType = 'cube',
  date,
  className = '',
  paddingClass = 'px-1 md:px-4 py-1 md:py-3',
  fontClassName,
}) => {
  const dispatch = useAppDispatch();

  const usedDate = parseCustomDate(date);
  const month = usedDate.toLocaleString('en-US', { month: 'short' });
  const year = usedDate.getFullYear().toString();
  const day = usedDate.getDate();

  const currentYear = new Date().getFullYear();
  const topBgClass = usedDate.getFullYear() === currentYear
    ? 'bg-red-500'
    : usedDate.getFullYear() === currentYear - 1
      ? 'bg-orange-400'
      : 'bg-gray-200';
  const topTextClass = (usedDate.getFullYear() === currentYear || usedDate.getFullYear() === currentYear - 1)
    ? 'text-white'
    : 'text-gray-900';
  const accentTextClass = usedDate.getFullYear() === currentYear
    ? 'text-red-500'
    : usedDate.getFullYear() === currentYear - 1
      ? 'text-orange-400'
      : 'text-gray-700';

  if (styleType === 'row-style-1') {
    return (
      <div className={`dv-date-icon-row flex items-center justify-center space-x-1 ${fontClassName ?? 'text-gray-800 text-base'} ${className}`}>
        <span className="">{day}</span>
        <span className="italic uppercase">{month}</span>
        <span className="">{year}</span>
      </div>
    );
  }
if (styleType === 'row-style-2') {
  return (
    <div
      className={`dv-date-icon-row flex flex-col items-center justify-center leading-none text-gray-800 ${className}`}
    >
      {/* day + month */}
      <div className="flex items-baseline space-x-1 text-base">
        <span>{day}</span>
        <span className="font-bold italic uppercase">{month}</span>
      </div>

      {/* year */}
      <div className="text-xs font-bold mt-0.5">
        {year}
      </div>
    </div>
  );
}

  return (
    <div className={`dv-date-icon w-fit h-auto flex flex-col rounded-lg shadow overflow-hidden text-gray-900 ${className}`}>
      <div className={`${topBgClass} ${topTextClass} text-center ${paddingClass} font-bold`}>
        {year}
      </div>
      <div className={`bg-gray-100 ${paddingClass} flex flex-col items-center`}>
        <div className="flex flex-row items-center">
          <div className={accentTextClass}>{day}</div>
          <div className={`font-bold uppercase pl-2 ${accentTextClass}`}>{month}</div>
        </div>
      </div>
    </div>
  );
};

export default UI_DateIcon;
