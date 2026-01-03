import './club-icon.css';
import { useAppDispatch } from '../../../../store/store';
import React from 'react';

interface UI_ClubIconProps {
  clubName: string;
  className?: string;
  iconWidth: string;
  styleType?: 'icon-notext' | 'icon-text-bottom' | 'icon-text-right';
}

const UI_ClubIcon: React.FC<UI_ClubIconProps> = ({
  clubName,
  className = '',
  iconWidth,
  styleType = 'icon-notext',
}) => {
  const dispatch = useAppDispatch();

  const base = import.meta.env.BASE_URL;
  const safeClubName = (clubName ?? '').trim();
  const fileBaseName = safeClubName
    .replaceAll(' ', '-')
    .replaceAll('"', '-')
    .replaceAll("'", '-')
    .replaceAll('/', '-')
    .replaceAll('\\', '-')
    .replaceAll('?', '-')
    .replaceAll('#', '-');

  const iconFileName = fileBaseName ? `${encodeURIComponent(fileBaseName)}.png` : 'no-club.png';
  const fallbackSrc = `${base}images/club-icon/no-club.png`;
  const imageSrc = `${base}images/club-icon/${iconFileName}`;
//console.log('imageSrc: ',imageSrc);
  const img = (
    <img
      src={imageSrc}
      alt={clubName}
      title={clubName}
      className={`w-${iconWidth} h-${iconWidth} object-contain`}
      onError={(e) => {
        if (e.currentTarget.src !== fallbackSrc) {
          e.currentTarget.src = fallbackSrc;
        }
      }}
    />
  );

  if (styleType === 'icon-text-bottom') {
    return (
      <div className={`dv-club-icon flex flex-col items-center space-y-1 text-gray-800 text-base  ${className}`}>
        {img}
        <span>{clubName}</span>
      </div>
    );
  }

  if (styleType === 'icon-text-right') {
    return (
      <div className={`dv-club-icon flex flex-row items-center gap-2 text-gray-800 text-base w-${iconWidth} h-${iconWidth} ${className}`}>
        {img}
        <span className="w-fit text-2xl">{clubName}</span>
      </div>
    );
  }

  return (
    <div className={`dv-club-icon h-auto flex items-center justify-center text-gray-900 w-${iconWidth} h-${iconWidth} ${className}`}>
      {img}
    </div>
  );
};

export default UI_ClubIcon;
