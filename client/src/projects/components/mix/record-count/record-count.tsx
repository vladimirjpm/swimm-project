import React, { useMemo } from 'react';
import { rootActions, useAppDispatch } from '../../../../store/store';
import { Enums } from '../../../../utils/interfaces/enums';
import HelperNormative from '../../../../utils/helpers/helper-normative';

interface UI_RecordCountProps {
  swimmerName: string;
}

const UI_RecordCount: React.FC<UI_RecordCountProps> = ({ swimmerName }) => {
  const dispatch = useAppDispatch();

  const records = useMemo(() => HelperNormative.getSwimmerRecords(swimmerName), [swimmerName]);
  const mastersCount = records.filter(r => r.isMasters).length;
  const ageCount = records.filter(r => !r.isMasters).length;

  if (records.length === 0) return null;

  const handleClick = () => {
    dispatch(rootActions.updateState({
      isPopup: true,
      popUpType: Enums.PopupType.swimmerRecords,
      popUpObj: { swimmerName, records },
    }));
  };

  return (
    <div
      className="flex flex-row gap-1 cursor-pointer hover:opacity-80"
      onClick={handleClick}
      title="Show records"
    >
      {mastersCount > 0 && (
        <span className="bg-yellow-400 text-black text-xs font-bold px-1.5 py-0.5 rounded">
          🏆{mastersCount}
        </span>
      )}
      {ageCount > 0 && (
        <span className="bg-orange-400 text-white text-xs font-bold px-1.5 py-0.5 rounded">
          🏆{ageCount}
        </span>
      )}
    </div>
  );
};

export default UI_RecordCount;
