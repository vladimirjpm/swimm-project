import './swimm-style-icon.css';
import { useAppDispatch } from '../../../../store/store';
import React from 'react';

interface UI_SwimmStyleIconProps {
  styleName: string;
  styleLen?: string;
  styleType?: 'icon-notext' | 'icon-text' | 'icon-len';
  className?: string; // ✅ Добавлен className
}

const UI_SwimmStyleIcon: React.FC<UI_SwimmStyleIconProps> = ({
  styleName,
  styleLen = '',
  styleType = 'icon-notext',
  className = '', // ✅ Значение по умолчанию
}) => {
  const dispatch = useAppDispatch();

  // Формируем путь к изображению
 let imageSrc;
 const base = import.meta.env.BASE_URL;
try {
  if (typeof styleName !== 'string') {
    throw new TypeError(`styleName должен быть string, а сейчас ${typeof styleName}`);
  }
  imageSrc = `${base}images/swimm-style-icon/${styleName.replaceAll(' ', '-')}.png`;
} catch (err) {
  console.error('Ошибка при формировании imageSrc:', err);
  // Запасной вариант, чтобы компонент не совсем упал
  imageSrc = `${base}images/swimm-style-icon/default.png`;
}

  const img = (
    <img
      src={imageSrc}
      alt={styleName}
      /* width={300} */
      className="object-contain"
      onError={(e) => {
        e.currentTarget.src = `${base}images/swimm-style-icon/no-swim.png`; // fallback картинка
      }}
    />
  );

  if (styleType === 'icon-text') {
    return (
      <div className={`dv-swimm-icon flex flex-col items-center space-y-1 text-gray-800 ${className}`}>
        {img}
        <span>{styleName}</span>
      </div>
    );
  }

  if (styleType === 'icon-len') {
    const len = String(styleLen ?? '');
    // Дистанции бывают четырёх- и пятизначные: чемпионат на 3 км в бассейне, открытая
    // вода (1600/5000/10000). Раньше подпись стояла ВЫШЕ своей коробки
    // (`margin-top: -10px`) и там, где у карточки `overflow: hidden`, её срезало.
    // Теперь она прижата внутрь угла; кегль уменьшается только у пятизначной — она одна
    // не помещается в самую узкую плитку (чип заплыва My media, 46px).

    return (
      <div className={`dv-swimm-icon relative flex flex-col items-center space-y-1 text-gray-800 ${className}`}>
        {img}
        <div className={`style-len text-red-700 ${len.length >= 5 ? 'style-len--xl' : ''}`}>
          {len}
        </div>
      </div>
    );
  }

  return (
    <div className={`dv-swimm-icon flex items-center justify-center shadow text-gray-900 ${className}`}>
      {img}
    </div>
  );
};

export default UI_SwimmStyleIcon;
