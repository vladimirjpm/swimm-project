import React from 'react';
import { useAppSelector } from '../../../store/store';
import UI_SwimmStyleIcon from '../mix/swimm-style-icon/swimm-style-icon';
import UI_PoolIcon from '../mix/pool-icon/pool-icon';

interface MastersRecord {
  time: string;
  name: string;
  club: string;
  record_date: string;
}

interface PopupData {
  genderKey: string;
  distanceData: Record<string, MastersRecord>;
  styleName: string;
  styleLen: string | number;
  poolType: string;
}

const GENDER_LABELS: Record<string, string> = {
  male: '♂',
  female: '♀',
};

const GENDER_STYLES: Record<string, { bg: string; border: string; title: string; header: string }> = {
  male: { bg: 'bg-blue-50', border: 'border-blue-200', title: 'text-blue-700', header: 'bg-blue-100' },
  female: { bg: 'bg-pink-50', border: 'border-pink-200', title: 'text-pink-700', header: 'bg-pink-100' },
};

function getStyles(genderKey: string) {
  return GENDER_STYLES[genderKey] ?? GENDER_STYLES.male;
}

const PopupContentMastersRecords: React.FC = () => {
  const popUpObj = useAppSelector((state) => state.popUpObj) as PopupData | PopupData[] | null;
  if (!popUpObj) return null;

  const items = Array.isArray(popUpObj) ? popUpObj : [popUpObj];

  return (
    <div>
      {items.map((item, itemIdx) => {
        const s = getStyles(item.genderKey);
        const ageGroupKeys = Object.keys(item.distanceData)
          .filter(k => /^\d+-\d+$/.test(k))
          .sort((a, b) => Number(a.split('-')[0]) - Number(b.split('-')[0]));

        if (ageGroupKeys.length === 0) return null;

        return (
          <div key={itemIdx} className="mb-4">
            <div className="flex items-center gap-3 mb-3">
              <span className={`text-lg font-bold ${s.title}`}>
                🏅 {items.length > 1 ? `${GENDER_LABELS[item.genderKey] || item.genderKey} ` : ''}ISR Masters Records
              </span>
              <UI_SwimmStyleIcon
                styleName={item.styleName}
                styleLen={String(item.styleLen)}
                styleType="icon-len"                
                className="font-bold text-3xl w-40"
              />
              <UI_PoolIcon
                styleType="icon-text-top"
                label={item.poolType}
                iconWidth="64"
                labelClassName="text-xs"
              />
            </div>
            <table className="w-full text-sm border-collapse">
              <thead>
                <tr className={`${s.header} text-left`}>
                  <th className="px-3 py-2 border">Age Group</th>
                  <th className="px-3 py-2 border">Time</th>
                  <th className="px-3 py-2 border">Name</th>
                  <th className="px-3 py-2 border">Club</th>
                  <th className="px-3 py-2 border">Date</th>
                </tr>
              </thead>
              <tbody>
                {ageGroupKeys.map(ageKey => {
                  const rec = item.distanceData[ageKey];
                  return (
                    <tr key={ageKey} className={`${s.bg} hover:opacity-80`}>
                      <td className="px-3 py-1.5 border font-medium">{ageKey}</td>
                      <td className="px-3 py-1.5 border font-bold">{rec.time}</td>
                      <td className="px-3 py-1.5 border">{rec.name}</td>
                      <td className="px-3 py-1.5 border text-gray-600">{rec.club}</td>
                      <td className="px-3 py-1.5 border text-gray-500">{rec.record_date}</td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        );
      })}
    </div>
  );
};

export default PopupContentMastersRecords;
