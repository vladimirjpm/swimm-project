import React from 'react';
import UI_MedalIcon from '../medal-icon/medal-icon';

type StyleType = 'filter' | 'mini' | 'full';

interface UI_ClubDetailsProps {
  club: string;
  isSelected: boolean;
  onSelect: (club: string) => void;
  gold: number;
  silver: number;
  bronze: number;
  swimmerCount: number;
  successfulCount: number;
  points: number;
  styleType?: StyleType;
}

const UI_ClubDetails: React.FC<UI_ClubDetailsProps> = ({
  club,
  isSelected,
  onSelect,
  gold,
  silver,
  bronze,
  swimmerCount,
  successfulCount,
  points,
  styleType = 'filter',
}) => {
  // Логируем все перед возвратом JSX
  /*console.log('UI_ClubDetails props:', {
    club,
    isSelected,
    gold,
    silver,
    bronze,
    swimmerCount,
    successfulCount,
    points,
    styleType,
  });*/

  switch (styleType) {
    case 'mini':
      return (
        <div className="flex items-center m-1">
          <button
            className={`w-40 px-3 py-1 border rounded ${isSelected ? 'bg-blue-500 text-white' : 'bg-white'}`}
            onClick={() => onSelect(club)}
          >
            {club}
          </button>
        </div>
      );

    case 'filter': {
      const iconSize = 'medal-24';
      return (
        <div className="flex items-center justify-start m-1">
          <button
            className={`w-40 px-3 py-1 border rounded ${isSelected ? 'bg-blue-500 text-white' : 'bg-white'}`}
            onClick={() => onSelect(club)}
          >
           <span>{club}</span>
          <span className="flex flex-row">
            <UI_MedalIcon place="1" styleType="icon-place" styleSize={iconSize} placeReplace={gold.toString()} />
            <UI_MedalIcon place="2" styleType="icon-place" styleSize={iconSize} placeReplace={silver.toString()} />
            <UI_MedalIcon place="3" styleType="icon-place" styleSize={iconSize} placeReplace={bronze.toString()} />
            <UI_MedalIcon place={(gold + silver + bronze).toString()} styleType="icon-noplace" styleSize={iconSize} />
          </span>
          </button>
          <div className='flex flex-col'>
          <span className="ml-2 text-lg font-bold text-gray-700">⭐ {points}</span>
          <span className="ml-2 text-sm text-gray-700">
            <span>🏊‍♂️ {swimmerCount}</span>
            <span>✅ {successfulCount}</span>
          </span></div>
        </div>
      );
    }

    case 'full': {
      const iconSize = 'medal-40';
      return (
        <div className="flex items-center justify-start m-1">
          <button
            className={`w-40 px-3 py-1 border rounded ${isSelected ? 'bg-blue-500 text-white' : 'bg-white'}`}
            onClick={() => onSelect(club)}
          >
            {club}
          </button>
          <span className="flex flex-row">
            <UI_MedalIcon place="1" styleType="icon-place" styleSize={iconSize} placeReplace={gold.toString()} />
            <UI_MedalIcon place="2" styleType="icon-place" styleSize={iconSize} placeReplace={silver.toString()} />
            <UI_MedalIcon place="3" styleType="icon-place" styleSize={iconSize} placeReplace={bronze.toString()} />
            <UI_MedalIcon place={(gold + silver + bronze).toString()} styleType="icon-noplace" styleSize={iconSize} />
          </span>
          <span className="ml-2 text-sm text-gray-700">
            <div>🏊‍♂️ {swimmerCount}</div>
            <div>✅ {successfulCount}</div>
          </span>
          <span className="ml-2 text-sm text-gray-700">⭐ {points}</span>
        </div>
      );
    }

    default:
      return null;
  }
};

export default UI_ClubDetails;
