import React from 'react';

type LevelProgressStyleType = 'progress-bar' | 'text-only';

interface UI_LevelProgressProps {
  className?: string;
  currentTime: string;
  nextTime: string | null;
  progressPercent: number | null;
  styleType?: LevelProgressStyleType;

  /** TEXT ONLY fonts */
  progressLabelClassName?: string;   // "Progress:"
  nextTimeClassName?: string;         // "to {nextTime}"
}

const UI_LevelProgress: React.FC<UI_LevelProgressProps> = ({
  className = '',
  currentTime,
  nextTime,
  progressPercent,
  styleType = 'progress-bar',

  progressLabelClassName = 'text-sm',
  nextTimeClassName = 'text-xs text-gray-600',
}) => {
  const hasNextLevel =
    typeof nextTime === 'string' && nextTime.trim().length > 0;

  const safeProgress =
    typeof progressPercent === 'number' && Number.isFinite(progressPercent)
      ? Math.min(100, Math.max(0, Math.round(progressPercent)))
      : null;

  /* ---------- PROGRESS BAR ---------- */
  if (styleType === 'progress-bar') {
    return (
      <div className={`col-span-2 ${className}`}>
        <div>
          <span className="text-base font-bold">{currentTime}</span>
          {hasNextLevel && (
            <>
              {' '}→ <span className="text-xs">{nextTime}</span>
            </>
          )}
        </div>

        {hasNextLevel && safeProgress !== null ? (
          <div className="w-full bg-gray-200 rounded-full dark:bg-gray-700 mt-1">
            <div
              className="bg-blue-600 text-xs font-medium text-blue-100 text-center p-0.5 leading-none rounded-full"
              style={{ width: `${safeProgress}%` }}
              role="progressbar"
              aria-valuenow={safeProgress}
              aria-valuemin={0}
              aria-valuemax={100}
            >
              {safeProgress}%
            </div>
          </div>
        ) : (
          <div className="text-xs text-gray-500 mt-1">
            Max level reached
          </div>
        )}
      </div>
    );
  }

  /* ---------- TEXT ONLY (DEFAULT) ---------- */
  return (
    <div className={`col-span-2 ${className}`}>
      <div className="flex items-center space-x-2">
        <span className={progressLabelClassName}>
          Progress:
        </span>

        {hasNextLevel && safeProgress !== null && (
          <>
            <span
              className="inline-flex items-center justify-center
                         rounded-full
                         bg-gray-100 text-blue-700
                         text-xs font-semibold
                         px-3 py-1
                         leading-none"
            >
              {safeProgress}%
            </span>

            <span className={nextTimeClassName}>
              to <span className="font-medium">{nextTime}</span>
            </span>
          </>
        )}

        {!hasNextLevel && (
          <span className="text-xs text-gray-500">
            Max level
          </span>
        )}
      </div>
    </div>
  );
};

export default UI_LevelProgress;
