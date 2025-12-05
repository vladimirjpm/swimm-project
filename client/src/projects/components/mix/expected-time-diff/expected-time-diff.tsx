import React from 'react';
import Helper from '../../../../utils/helpers/data-helper';

interface ExpectedTimeDiffProps {
  time: string;
  expected_time?: string;
}

const ExpectedTimeDiff: React.FC<ExpectedTimeDiffProps> = ({ time, expected_time }) => {
  if (!expected_time) return null;

  const actualSec = Helper.parseTimeToSeconds(time);
  const expectedSec = Helper.parseTimeToSeconds(expected_time);
  const diff = actualSec - expectedSec;
  const diffAbs = Math.abs(diff);
  const diffStr = Helper.formatSecondsToTimeString(diffAbs);
  
  // diff > 0 означает actual > expected (медленнее) — красный
  // diff < 0 означает actual < expected (быстрее) — зелёный
  const colorClass = diff > 0 ? 'text-red-600' : diff < 0 ? 'text-green-600' : 'text-gray-500';
  const sign = diff > 0 ? '+' : diff < 0 ? '-' : '';
  
  // Форматируем expected_time без десятых (округляем вниз)
  const expectedFloor = Math.floor(expectedSec);
  const mins = Math.floor(expectedFloor / 60);
  const secs = expectedFloor % 60;
  const expectedDisplay = mins > 0 
    ? `${mins}:${secs.toString().padStart(2, '0')}` 
    : `${secs}`;

  return (
    <div className="text-xs text-gray-500">
      <span>({expectedDisplay})</span>
      {Number.isFinite(diff) && (
        <span className={`ml-1 font-semibold ${colorClass}`}>
          {sign}{diffStr}
        </span>
      )}
    </div>
  );
};

export default ExpectedTimeDiff;
