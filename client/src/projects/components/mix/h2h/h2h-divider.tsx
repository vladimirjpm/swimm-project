import React from 'react';
import './h2h.css';

/**
 * Разделитель между общими и односторонними заплывами (макет 1b): «ONLY ONE SWIMMER».
 * Нужен, потому что дальше идут карточки без разрыва — без подписи они читаются как
 * поломка расчёта, а не как «второй эту дистанцию не плавал».
 */
const UI_H2HDivider: React.FC<{ text: string }> = ({ text }) => (
  <div className="h2h-divider">
    <span className="h2h-divider__line" />
    <span className="h2h-divider__text">{text}</span>
    <span className="h2h-divider__line" />
  </div>
);

export default UI_H2HDivider;
