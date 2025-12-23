import './date-icon.css';
import { useAppDispatch } from '../../../../store/store';
import React from 'react';

interface UI_DateIconProps {
  styleType?: 'cube'  | 'row-style-1' | 'row-style-2';
  date?: string; // формат: 'DD-MM-YYYY'
  paddingClass?: string;
  className?: string;
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
}) => {
  const dispatch = useAppDispatch();

  const usedDate = parseCustomDate(date);
  const month = usedDate.toLocaleString('en-US', { month: 'short' });
  const year = usedDate.getFullYear().toString();
  const day = usedDate.getDate();

  if (styleType === 'row-style-1') {
    return (
      <div className={`dv-date-icon-row flex items-center justify-center space-x-1 text-gray-800 text-base ${className}`}>
        <span className="">{day}</span>
        <span className="font-bold italic uppercase">{month}</span>
        <span className="font-bold">{year}</span>
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
      <div className={`bg-red-500 text-white text-center ${paddingClass} font-bold`}>
        {year}
      </div>
      <div className={`bg-gray-100 ${paddingClass} flex flex-col items-center`}>
        <div className="flex flex-row items-center">
          <div className="text-red-500">{day}</div>
          <div className="font-bold uppercase pl-2 text-red-500">{month}</div>
        </div>
      </div>
    </div>
  );
};

export default UI_DateIcon;
