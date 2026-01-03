import './normative-level-icon.css';
import { rootActions, useAppDispatch } from '../../../../store/store';
import React from 'react';
import { Enums } from '../../../../utils/interfaces/enums';

interface UI_NormativeLevelIconProps {
  levelName: string;
  styleType: string;//component style
  styleSize: string;
  className?: string;
  styleName?: string;//swimm style name
  styleLen?: string;//swimm style len
  poolType?: string;//swimm pool len
  normativeAgeGroup?: string | null;//masters age group key e.g. "25-29"
  isMasters?: boolean | null;//is masters normative
}

const getMedalClassAndLabel = (levelName: string): { label: string; className: string } => {
  switch (levelName) {
    case 'I_youth':
      return { label: '1y', className: 'normative-medal gold' };
    case 'II_youth':
      return { label: '2y', className: 'normative-medal silver' };
    case 'III_youth':
      return { label: '3y', className: 'normative-medal bronze' };
    case 'I_adult':
      return { label: 'I', className: 'normative-medal gold' };
    case 'II_adult':
      return { label: 'II', className: 'normative-medal silver' };
    case 'III_adult':
      return { label: 'III', className: 'normative-medal bronze' };
    case 'KMS':
      return { label: 'KMS', className: 'normative-medal bronze' };
    case 'MS':
      return { label: 'MS', className: 'normative-medal silver' };
    case 'MSMK':
      return { label: 'MSMK', className: 'normative-medal gold' };
    default:
      return { label: levelName, className: 'normative-medal' };
  }
};

const UI_NormativeLevelIcon: React.FC<UI_NormativeLevelIconProps> = ({
  levelName,
  styleType = '',
  styleSize = 'size-1',
  className = '',
  styleName, 
  styleLen, 
  poolType,
  normativeAgeGroup,
  isMasters,
}) => {
  const dispatch = useAppDispatch();
  const handleNormativeClick = () => {
        dispatch(
      rootActions.updateState({
          isPopup: true,
          popUpType: Enums.PopupType.normative,
          popUpObj: {levelName, styleName, styleLen, poolType, isMasters: isMasters ?? false, normativeAgeGroup}
      })
    );
  };

  if (styleType === '') {
    return (
      <div
        className={`dv-normative-level-icon text-gray-800 text-base ${className}}`}
        onClick={handleNormativeClick}
      >
        NONE
      </div>
    );
  }

  const { label, className: medalClass } = getMedalClassAndLabel(levelName);

  return (
    <div
      className={`dv-normative-level-icon cursor-pointer ${className}`}
      onClick={handleNormativeClick}
    >
      <div className={`${medalClass} ${styleSize} level-${levelName.toLowerCase()}`}>{label}</div>
      {normativeAgeGroup && (
        <div className="normative-age-group font-semibold">{normativeAgeGroup}</div>
      )}
    </div>
  );
};

export default UI_NormativeLevelIcon;