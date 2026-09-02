import React from 'react';
import './h2h.css';

/**
 * «Поменять стороны местами» — кнопка МЕЖДУ карточками: она про пару, а не про одну из
 * сторон, поэтому стоит в центральной колонке под счётом (или под «vs», пока счёта нет).
 *
 * У таба страницы пловца её нет вовсе: слева там хозяин профиля, и «поменять местами»
 * означало бы уехать с его страницы.
 */
const UI_H2HSwap: React.FC<{ onSwap?: () => void }> = ({ onSwap }) => {
  if (!onSwap) return null;
  return (
    <button type="button" className="h2h-swap" title="Swap sides" onClick={onSwap}>
      ⇄
    </button>
  );
};

export default UI_H2HSwap;
