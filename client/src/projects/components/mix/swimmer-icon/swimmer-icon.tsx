import './swimmer-icon.css';
import { useAppDispatch } from '../../../../store/store';
import React from 'react';

interface UI_SwimmerIconProps {
  swimmerCode: string;
  swimmerGender: string;
  iconWidth: string;
  styleType?: 'icon-notext' | 'icon-text';
}

const UI_SwimmerIcon: React.FC<UI_SwimmerIconProps> = ({ swimmerCode,swimmerGender, iconWidth, styleType = 'icon-notext' }) => {
  const dispatch = useAppDispatch();
  // Формируем путь к изображению
  const base = import.meta.env.BASE_URL;
  const imageSrc = swimmerCode
  ? `${base}images/swimmers/${swimmerCode.replaceAll(' ', '-')}.png`
  : `${base}images/swimmers/default-${swimmerGender}.png`;

  const img = (
    <img
      src={imageSrc}
      alt={swimmerCode}
      className={`w-${iconWidth} h-${iconWidth} object-contain`}
      onError={(e) => {
        e.currentTarget.src = `${base}images/swimmers/default-${swimmerGender}.png`; // fallback картинка
      }}
    />
  );

  if (styleType === 'icon-text') {
    return (
    <div className="dv-swimmer-icon flex flex-col items-center space-y-1 text-gray-800 text-base">
      {img}
      <span>{swimmerCode}</span>
    </div>
    );
  }

  return (
    <div className="dv-swimmer-icon bg-gray-100 rounded-lg w-fit h-auto flex items-center justify-center text-gray-900">
      {img}
    </div>
  );
};

export default UI_SwimmerIcon;
