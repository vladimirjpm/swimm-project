import './date-icon.css';
import { useAppDispatch } from '../../../../store/store';
import React from 'react';
import UI_PrelimLabel from '../prelim-label/prelim-label';

interface UI_DateIconProps {
  styleType?: 'cube'  | 'row-style-1' | 'row-style-2';
  /**
   * Дата в ОДНОМ из двух видов: 'DD/MM/YYYY' (таблица результатов, статика)
   * или ISO 'YYYY-MM-DD' (API страницы пловца, My media). Два формата живут в
   * продукте реально, и разбирать их обязан компонент — иначе каждый вызывающий пишет
   * своё преобразование (или печатает сырую ISO-строку, как было в My media).
   */
  date?: string;
  paddingClass?: string;
  className?: string;
  fontClassName?: string; // переопределяет текстовый стиль (size/weight/color) для row-style-1
  /** Индикатор тумблера предварительных заплывов под кубом даты (сводка фильтров):
   *  'on' → зелёный [prelim ON], 'off' → оранжевый [prelim OFF]. Не задан — ничего.
   *  Текст/цвета живут в UI_PrelimLabel — тут только место в раскладке. */
  prelimState?: 'on' | 'off';
}

const parseCustomDate = (dateStr?: string): Date => {
  if (!dateStr) return new Date();

  // ISO 'YYYY-MM-DD' — так дату отдаёт API. Разбираем полями, а не `new Date(str)`:
  // конструктор читает ISO-дату как UTC и в отрицательных часовых поясах сдвигает день назад.
  const iso = /^(\d{4})-(\d{2})-(\d{2})$/.exec(dateStr);
  if (iso) return new Date(Number(iso[1]), Number(iso[2]) - 1, Number(iso[3]));

  const [day, month, year] = dateStr.split('/').map(Number);
  return new Date(year, month - 1, day);
};

const UI_DateIcon: React.FC<UI_DateIconProps> = ({
  styleType = 'cube',
  date,
  className = '',
  paddingClass = 'px-1 md:px-4 py-1 md:py-3',
  fontClassName,
  prelimState,
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

  const cube = (
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

  // Обёртка появляется только под индикатор — остальные вызовы получают прежнюю разметку.
  if (!prelimState) return cube;
  return (
    <div className="flex flex-col items-center gap-1">
      {cube}
      <UI_PrelimLabel state={prelimState} className="text-[10px]" />
    </div>
  );
};

export default UI_DateIcon;
