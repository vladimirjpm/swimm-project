import './medal-icon.css';
import { useAppDispatch } from '../../../../store/store';
import React from 'react';

interface UI_MedalIconProps {
  place: string; // '1', '2', '3'
  placeReplace?: string | undefined;
  styleType?: 'icon-noplace' | 'icon-place';
  styleSize?: 'medal-16' |'medal-24' | 'medal-40';
  title? :string | undefined;
}

const UI_MedalIcon: React.FC<UI_MedalIconProps> = ({ place, styleType= 'icon-place', styleSize= 'medal-40',title= "" ,placeReplace }) => {
  const dispatch = useAppDispatch();

  // Выбор цвета медали по месту
  let medalClass = '';
  switch (place) {
    case '1':
      medalClass = 'gold';
      break;
    case '2':
      medalClass = 'silver';
      break;
    case '3':
      medalClass = 'bronze';
      break;
    default:
      medalClass = '';
  }
  if(placeReplace && placeReplace === '0')
    medalClass = 'none';
  if(styleType === 'icon-noplace')
    medalClass = 'none';

  return (
    <div className="dv-medal-icon" title={title}>
      <div className={`medal ${medalClass}  ${styleSize}`}>{placeReplace ? placeReplace : place}</div>
    </div>
  );
};

export default UI_MedalIcon;
