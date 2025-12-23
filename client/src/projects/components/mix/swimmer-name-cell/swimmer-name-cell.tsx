import React from 'react';

interface SwimmerNameCellProps {
  firstName: string;
  lastName?: string;
  club?: string;
  isRelay?: boolean;
  relaySwimmersName?: string;
  onClick?: () => void;
  firstLineClassName?: string;
  secondLineClassName?: string;
  className?: string;
}

const SwimmerNameCell: React.FC<SwimmerNameCellProps> = ({
  firstName,
  lastName,
  club,
  isRelay,
  relaySwimmersName,
  onClick,
  firstLineClassName = 'text-xl font-bold',
  secondLineClassName = 'text-xs',
  className = '',
}) => {
  const displayName = isRelay
    ? club || 'Relay Team'
    : `${firstName}${lastName ? ` - ${lastName}` : ''}`;

  const subText = isRelay ? relaySwimmersName : club;

  return (
    <div className={`${onClick ? 'cursor-pointer' : ''} ${className}`} onClick={onClick}>
      <div className={firstLineClassName}>{displayName}</div>
      {subText && <div className={secondLineClassName}>{subText}</div>}
    </div>
  );
};

export default SwimmerNameCell;
