import React from 'react';
import { useAppSelector } from '../../../store/store';
import { SwimmerRecord } from '../../../utils/helpers/helper-normative';
import UI_SwimmStyleIcon from '../mix/swimm-style-icon/swimm-style-icon';
import UI_PoolIcon from '../mix/pool-icon/pool-icon';

interface PopupData {
  swimmerName: string;
  records: SwimmerRecord[];
}

const GENDER_LABELS: Record<string, string> = {
  male: '♂',
  female: '♀',
};

const PopupContentSwimmerRecords: React.FC = () => {
  const popUpObj = useAppSelector((state) => state.popUpObj) as PopupData | null;
  if (!popUpObj || !popUpObj.records?.length) return null;

  const { swimmerName, records } = popUpObj;
  const mastersRecords = records.filter(r => r.isMasters);
  const ageRecords = records.filter(r => !r.isMasters);

  const renderTable = (items: SwimmerRecord[], title: string, bgClass: string, headerClass: string) => {
    if (items.length === 0) return null;
    return (
      <div className="mb-4">
        <div className="text-lg font-bold mb-2">{title} ({items.length})</div>
        <table className="w-full text-sm border-collapse">
          <thead>
            <tr className={`${headerClass} text-left`}>
              <th className="px-3 py-2 border">Gender</th>
              <th className="px-3 py-2 border">Style</th>
              <th className="px-3 py-2 border">Distance</th>
              <th className="px-3 py-2 border">Pool</th>
              <th className="px-3 py-2 border">Age</th>
              <th className="px-3 py-2 border">Time</th>
              <th className="px-3 py-2 border">Club</th>
              <th className="px-3 py-2 border">Date</th>
            </tr>
          </thead>
          <tbody>
            {items.map((rec, idx) => (
              <tr key={idx} className={`${bgClass} hover:opacity-80`}>
                <td className="px-3 py-1.5 border">{GENDER_LABELS[rec.gender] ?? rec.gender}</td>
                <td className="px-3 py-1.5 border">
                  <UI_SwimmStyleIcon
                    styleName={rec.style}
                    styleLen={rec.distance}
                    styleType="icon-len"
                    className="text-sm w-24"
                  />
                </td>
                <td className="px-3 py-1.5 border">{rec.distance}</td>
                <td className="px-3 py-1.5 border">
                  <UI_PoolIcon
                    styleType="icon-text-top"
                    label={rec.pool}
                    iconWidth="32"
                    labelClassName="text-xs"
                  />
                </td>
                <td className="px-3 py-1.5 border font-medium">{rec.ageKey}</td>
                <td className="px-3 py-1.5 border font-bold">{rec.time}</td>
                <td className="px-3 py-1.5 border text-gray-600">{rec.club}</td>
                <td className="px-3 py-1.5 border text-gray-500">{rec.recordDate}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    );
  };

  return (
    <div>
      <div className="text-xl font-bold mb-4">🏆 {swimmerName} — Records</div>
      {renderTable(mastersRecords, 'Masters Records', 'bg-yellow-50', 'bg-yellow-100')}
      {renderTable(ageRecords, 'Age Records', 'bg-orange-50', 'bg-orange-100')}
    </div>
  );
};

export default PopupContentSwimmerRecords;
