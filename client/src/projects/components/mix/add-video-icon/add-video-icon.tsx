import React from 'react';

interface Props {
  size?: number;
  className?: string;
}

/**
 * Видеокамера с плюсиком — «добавить видео» (тумблер в шапке результатов и
 * иконка на строке). Красится через currentColor — цвет задаёт родитель.
 */
const UI_AddVideoIcon: React.FC<Props> = ({ size = 16, className }) => (
  <svg
    width={size}
    height={size}
    viewBox="0 0 24 24"
    fill="none"
    stroke="currentColor"
    strokeWidth={2}
    strokeLinecap="round"
    strokeLinejoin="round"
    className={className}
    aria-hidden
  >
    {/* корпус камеры + объектив-трапеция */}
    <rect x="2" y="7" width="13" height="12" rx="2.5" />
    <path d="m15 11 5-3v10l-5-3" />
    {/* плюсик внутри корпуса */}
    <path d="M8.5 10.5v5M6 13h5" />
  </svg>
);

export default UI_AddVideoIcon;
