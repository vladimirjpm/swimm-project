import React, { useState } from 'react';
import './intensity-icon.css';
import { useAppDispatch } from '../../../../store/store';
import { createPortal } from 'react-dom';

interface UI_IntensityIconProps {
  intensity?: string;
  className?: string;
  iconWidth?: string;
  styleType?: 'icon-nocolor';
  label?: string;
}

const UI_IntensityIcon: React.FC<UI_IntensityIconProps> = ({
  intensity = 'v0',
  className = '',
  iconWidth = '8',
  styleType = '',
  label = 'Effort',
}) => {
  const dispatch = useAppDispatch();
  const [showPopup, setShowPopup] = useState(false);

  const normalized = intensity.toLowerCase();

  const handleClick = () => setShowPopup(true);

  if (styleType === 'icon-nocolor') {
    return (
      <span
        className="dv-intensity-icon px-2 py-0.5 rounded-xl text-sm font-semibold bg-gray-100 border cursor-pointer hover:bg-gray-200"
        onClick={handleClick}
        title="Show intensity chart"
      >
        {intensity.toUpperCase()}
      </span>
    );
  }

  return (
    <>
      <span
        className={`dv-intensity-icon intensity-${normalized} ${className} px-2 py-0.5 rounded-xl text-sm font-semibold border cursor-pointer hover:brightness-110 transition`}
        onClick={handleClick}
        title="Show intensity chart"
      >
        {intensity.toUpperCase()}
      </span>

      {/* Простой попап */}
      {showPopup &&
        createPortal(
          <div
            className="fixed inset-0 z-[9999] bg-black/60 flex items-center justify-center"
            onClick={() => setShowPopup(false)}
          >
            <div
              className="relative bg-white rounded-xl shadow-xl p-2 max-w-3xl"
              onClick={(e) => e.stopPropagation()} // чтобы клик по картинке не закрывал
            >
              <img
                src={`${import.meta.env.BASE_URL}images/training/intensity.jpg`}
                alt="Intensity levels"
                className="max-h-[80vh] rounded-lg"
              />
              <button
                onClick={() => setShowPopup(false)}
                className="absolute top-1 right-2 text-gray-600 text-2xl leading-none hover:text-black"
                aria-label="Close"
              >
                ×
              </button>
            </div>
          </div>,
          document.body
        )
      }
    </>
  );
};

export default UI_IntensityIcon;
