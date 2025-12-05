// training-set-header: renders the small block header used in training-show-full-table
import React, { useState, useEffect } from 'react';
import { Result } from '../../../utils/interfaces/results';
import UI_DateIcon from '../../components/mix/date-icon/date-icon';
import TrainingSetHeaderImages from '../training-set-heager-images/training-set-header-images';

export interface TrainingSetHeaderProps {
  trainingName?: string;
  date?: string;
  interval?: number;
  pool_type?: string;
  trainingId?: number;
  className?: string;
  header?: {
    firstResult?: Result;
    showEvent?: boolean;
    showDate?: boolean;
  };
}

export default function TrainingSetHeader({
  trainingName,
  date,
  interval,
  pool_type,
  className,
  header,
  trainingId,
}: TrainingSetHeaderProps) {
  const [showT, setShowT] = useState(true);
  const [showR, setShowR] = useState(true);
  const [modalSrc, setModalSrc] = useState<string | null>(null);
  return (
    <div className={`${className ?? ''}`}>
      {/* ===== Шапка блока ===== */}
      <div className="p-2 md:p-4 flex flex-col items-center justify-between mb-2 text-sm text-gray-700 bg-gray-400">
        <div className='flex items-center justify-between w-full'>
        <div className="mb-4 text-3xl md:text-5xl effect-super-bold text-center">{trainingName}</div>
        <div className="flex items-center gap-2">
          {header?.showDate && (
            <UI_DateIcon styleType="cube" className="text-base" paddingClass="px-1 py-1" date={date} />
          )}
        </div>

        </div>
        <div className="flex items-center justify-start gap-4 w-full">
          <TrainingSetHeaderImages trainingId={trainingId} thumbClass="w-16 h-16 md:w-20 md:h-20 object-cover rounded" />
          <div className="flex flex-col text-white">
            {/* {typeof interval === 'number' && (
              <div>
                <b>Interval:</b> {interval}s
              </div>
            )} */}
            {/* {pool_type && (
              <div>
                <b>Pool:</b> {pool_type}m
              </div>
            )} */}
          </div>
        </div>
        
      </div>
    </div>
  );
}