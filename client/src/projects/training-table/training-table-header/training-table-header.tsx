// src/ui/training/TrainingTableHeader.tsx
import React from 'react';
import UI_DateIcon from '../../components/mix/date-icon/date-icon';
import UI_ClubIcon from '../../components/mix/club-icon/club-icon';
import UI_SwimmStyleIcon from '../../components/mix/swimm-style-icon/swimm-style-icon';
import { Result } from '../../../utils/interfaces/results';

export interface TrainingTableHeaderProps {
  title?: string;
  firstResult?: Result;
  showEvent: boolean;
  showDate: boolean;
  className?: string;
}

export default function TrainingTableHeader({
  title,
  firstResult,
  showEvent,
  showDate,
  className,
}: TrainingTableHeaderProps) {
  return (
    <div className={`${className ?? 'my-4 px-2'} `}>

      {/* Дата / Клуб */}
      <div className="mb-2 text-sm text-gray-700 flex justify-between items-top relative">
        <div className="flex flex-col space-y-1">
          {!showDate && firstResult?.date && (
            <div>
              <UI_DateIcon 
              styleType="cube" 
              className='text-xs md:text-xl'
              paddingClass='px-1 md:px-2 py-1 md:py-2'
              date={firstResult.date} 
              />
            </div>
          )}
        </div>

      {
      title && 
      <h2 className="belive-effect text-center mb-2">
          {title}
          <span>{title}</span>
          <span>{title}</span>
          <span>with Anna GOSTOMELSKY</span>
      </h2>
      
      }
        <div className="pl-4 whitespace-nowrap">
          {firstResult?.club && (
            <UI_ClubIcon
              clubName={firstResult.club}
              iconWidth="20"
              className='w-15 md:w-auto'
              styleType="icon-notext"
            />
          )}
        </div>
      </div>

      {/* Крупная иконка стиля, если все строки одного события */}
      <div className="flex flex-wrap">
        {!showEvent && firstResult && (
          <div className="w-fit mx-auto">
            <UI_SwimmStyleIcon
              styleName={firstResult.event_style_name}
              styleLen={firstResult.event_style_len}
              styleType="icon-len"
              className="font-bold text-6xl w-64"
            />
          </div>
        )}
      </div>
    </div>
  );
}
