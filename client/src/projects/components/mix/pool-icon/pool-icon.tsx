import React from 'react';
import './pool-icon.css';
import { useAppDispatch } from '../../../../store/store';

interface UI_PoolIconProps {
  className?: string;
  iconWidth?: string;
  styleType?: 'icon-notext' | 'icon-text-bottom' | 'icon-text-top' | 'icon-text-center';
  label?: string;
  labelClassName?: string;
}

const UI_PoolIcon: React.FC<UI_PoolIconProps> = ({
  className = '',
  iconWidth = '64',
  styleType = 'icon-notext',
  label = 'Pool',
  labelClassName = 'text-base',
}) => {
  const dispatch = useAppDispatch();

  const svg = (
    <svg
      viewBox="0 0 366 170"
      width={iconWidth}
      height="auto"
      xmlns="http://www.w3.org/2000/svg"
      className={`object-contain ${className}`}
    >
      {/* waves */}
      <g
        fill="none"
        stroke="#32B1CC"
        strokeWidth="8"
        strokeLinecap="round"
      >
        <path d="M10 65 C40 45,70 85,100 65 S160 85,190 65 S250 85,280 65 S340 85,360 65" />
        <path d="M10 95 C40 75,70 115,100 95 S160 115,190 95 S250 115,280 95 S340 115,360 95" />
      </g>

      {/* elongated pills */}
      <g transform="translate(25,135)">
        <ellipse cx="0" cy="0" rx="11" ry="18" fill="#1C5C85" />
        <ellipse cx="18" cy="0" rx="11" ry="18" fill="#1C5C85" />
        <ellipse cx="36" cy="0" rx="11" ry="18" fill="#1C5C85" />
        <ellipse cx="54" cy="0" rx="11" ry="18" fill="#216492" />
        <ellipse cx="72" cy="0" rx="11" ry="18" fill="#87CEE9" />
        <ellipse cx="90" cy="0" rx="11" ry="18" fill="#216492" />
        <ellipse cx="108" cy="0" rx="11" ry="18" fill="#1C5C85" />
        <ellipse cx="126" cy="0" rx="11" ry="18" fill="#216492" />
        <ellipse cx="144" cy="0" rx="11" ry="18" fill="#87CEE9" />
        <ellipse cx="162" cy="0" rx="11" ry="18" fill="#1C5C85" />
        <ellipse cx="180" cy="0" rx="11" ry="18" fill="#1C5C85" />
        <ellipse cx="198" cy="0" rx="11" ry="18" fill="#87CEE9" />
        <ellipse cx="216" cy="0" rx="11" ry="18" fill="#216492" />
        <ellipse cx="234" cy="0" rx="11" ry="18" fill="#1C5C85" />
        <ellipse cx="252" cy="0" rx="11" ry="18" fill="#216492" />
        <ellipse cx="270" cy="0" rx="11" ry="18" fill="#87CEE9" />
        <ellipse cx="288" cy="0" rx="11" ry="18" fill="#1C5C85" />
        <ellipse cx="306" cy="0" rx="11" ry="18" fill="#216492" />
        <ellipse cx="324" cy="0" rx="11" ry="18" fill="#1C5C85" />
      </g>
    </svg>
  );

  if (styleType === 'icon-text-bottom') {
    return (
      <div className="dv-PoolIcon-icon flex flex-col items-center space-y-1 text-gray-800 text-base">
        {svg}
        <span className={labelClassName}>{label}</span>
      </div>
    );
  }

  if (styleType === 'icon-text-top') {
    return (
      <div className="dv-PoolIcon-icon flex flex-col items-center text-gray-800 text-base">
        <div className={labelClassName}>{label}</div>
        {svg}
      </div>
    );
  }
   if (styleType === 'icon-text-center' && (label === '25' || label === '25m' || label === '50' || label === '50m')) {
    let decoratedLabel = label;
    if (label === '25' || label === '25m') {
      decoratedLabel = `--${label}--`;
    } else if (label === '50' || label === '50m') {
      decoratedLabel = `-----${label}-----`;
    }

    return (
      <div className="dv-PoolIcon-icon flex flex-col items-center text-gray-800 text-base">
        <div className={labelClassName}>{decoratedLabel}</div>
      </div>
    );
  }

  return (
    <div
      className="dv-PoolIcon-icon w-fit h-auto flex items-center justify-center"
      title={label}
    >
      {svg}
    </div>
  );
};

export default UI_PoolIcon;
