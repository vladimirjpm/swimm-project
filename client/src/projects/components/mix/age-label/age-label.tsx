import React from 'react';

interface UI_AgeLabelProps {
  age: string | number;
  isMasters?: boolean;
  ageGroup?: string;
  className?: string;
}

const UI_AgeLabel: React.FC<UI_AgeLabelProps> = ({ age, isMasters = false, ageGroup, className = '' }) => {
  return (
    <div className={`flex flex-col ${className}`}>
      <div className="font-bold text-sm mt-1">
        <span className="font-normal text-xs">age:</span> {age}
      </div>
      {isMasters && ageGroup && (
        <div className="font-normal text-xs text-red-700">
          [{ageGroup}]
        </div>
      )}
    </div>
  );
};

export default UI_AgeLabel;
