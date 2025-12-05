import React from 'react';

interface Toggle3OptionsProps {
  id?: string;
  value: 'no' | 'regular' | 'masters';
  onChange: (v: 'no' | 'regular' | 'masters') => void;
  labels?: { left: string; middle: string; right: string };
  // Tailwind class strings
  labelClass?: string; // applied to all labels
  labelActiveClass?: string; // applied to the active label
  selectorClass?: string; // applied to the selector element
  selectorColorClasses?: { no?: string; regular?: string; masters?: string };
  containerClass?: string;
}

const Toggle3Options: React.FC<Toggle3OptionsProps> = ({
  id = 'toggle3',
  value,
  onChange,
  labels = { left: 'No', middle: 'Regular', right: 'Masters' },
  labelClass = 'text-xs px-3',
  labelActiveClass = 'text-white',
  selectorClass = '',
  selectorColorClasses = {
    no: 'bg-green-500',
    regular: 'bg-yellow-400',
    masters: 'bg-red-500',
  },
  containerClass = '',
}) => {
  const name = `${id}-group`;
  const options: Array<'no' | 'regular' | 'masters'> = ['no', 'regular', 'masters'];
  const index = options.indexOf(value);
  const translateX = `${index * 100}%`;

  const selectorBgClass = selectorColorClasses[value] || '';

  return (
    <div className={`${containerClass} inline-block`}> 
      <div className="relative rounded-full bg-gray-200 h-6 overflow-hidden">
        {/* Hidden inputs for accessibility */}
        <input
          id={`${id}-no`}
          name={name}
          type="radio"
          value="no"
          className="sr-only"
          checked={value === 'no'}
          onChange={() => onChange('no')}
          aria-checked={value === 'no'}
        />
        <input
          id={`${id}-regular`}
          name={name}
          type="radio"
          value="regular"
          className="sr-only"
          checked={value === 'regular'}
          onChange={() => onChange('regular')}
          aria-checked={value === 'regular'}
        />
        <input
          id={`${id}-masters`}
          name={name}
          type="radio"
          value="masters"
          className="sr-only"
          checked={value === 'masters'}
          onChange={() => onChange('masters')}
          aria-checked={value === 'masters'}
        />

        {/* Selector */}
        <span
          className={`${selectorClass} ${selectorBgClass} absolute top-0 left-0 h-full rounded-full`}
          style={{ width: 'calc(100% / 3)', transform: `translateX(${translateX})`, transition: 'transform .25s ease' }}
          aria-hidden
        />

        {/* Labels laid out with flex: each takes equal width */}
        <div className="relative z-10 flex h-full w-full text-center select-none">
          <label htmlFor={`${id}-no`} className={`flex-1 flex items-center justify-center ${labelClass} ${value === 'no' ? labelActiveClass : ''}`}>
            {labels.left}
          </label>
          <label htmlFor={`${id}-regular`} className={`flex-1 flex items-center justify-center ${labelClass} ${value === 'regular' ? labelActiveClass : ''}`}>
            {labels.middle}
          </label>
          <label htmlFor={`${id}-masters`} className={`flex-1 flex items-center justify-center ${labelClass} ${value === 'masters' ? labelActiveClass : ''}`}>
            {labels.right}
          </label>
        </div>
      </div>
    </div>
  );
};

export default Toggle3Options;
