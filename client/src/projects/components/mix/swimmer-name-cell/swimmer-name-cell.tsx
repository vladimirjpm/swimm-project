import React from 'react';
import { RelaySwimmer } from '../../../../utils/interfaces/results';
import UI_ClubIcon from '../club-icon/club-icon';

interface SwimmerNameCellProps {
  firstName: string;
  lastName?: string;
  firstNameEn?: string;
  lastNameEn?: string;
  club?: string;
  isRelay?: boolean;
  relaySwimmersName?: string;
  relaySwimmersList?: RelaySwimmer[];
  onClick?: () => void;
  firstLineClassName?: string;
  secondLineClassName?: string;
  className?: string;
  showClubIcon?: boolean;
}

const SwimmerNameCell: React.FC<SwimmerNameCellProps> = ({
  firstName,
  lastName,
  firstNameEn,
  lastNameEn,
  club,
  isRelay,
  relaySwimmersName,
  relaySwimmersList,
  onClick,
  firstLineClassName = 'text-xl font-bold',
  secondLineClassName = 'text-xs',
  className = '',
  showClubIcon = false,
}) => {
  const displayName = isRelay
    ? club || 'Relay Team'
    : `${firstName}${lastName ? ` ${lastName}` : ''}`;

  const hasEnglishName =
    Boolean(firstNameEn && firstNameEn.trim()) ||
    Boolean(lastNameEn && lastNameEn.trim());

  // Формируем subText для эстафеты
  let subText: string | undefined;
  let relayList: React.ReactNode = null;
  if (isRelay) {
    if (relaySwimmersList && relaySwimmersList.length > 0) {
      relayList = (
        <div className={secondLineClassName}>
          {relaySwimmersList.map((s, i) => (
            <div key={i}>
              {s.first_name} {s.last_name}{s.birth_year ? ` (${s.birth_year})` : ''}
            </div>
          ))}
        </div>
      );
    } else {
      subText = relaySwimmersName;
    }
  } else {
    subText = club;
  }

  return (
    <div className={`${className}`} onClick={onClick}>
    <div className={`${onClick ? 'cursor-pointer' : ''}`}>
      <div className={firstLineClassName}>{displayName}</div>
      {relayList}
      {subText && <div className={`flex items-center ${secondLineClassName}`}>        
          {showClubIcon && club && (
          <UI_ClubIcon clubName={club} className="pr-2 text-xs inline-block mt-0.5" iconWidth="10" styleType="icon-notext" />)}
        {subText}
      </div>}
    </div>
    </div>
  );
};

export default SwimmerNameCell;
