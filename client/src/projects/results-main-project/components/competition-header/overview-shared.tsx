import React from 'react';

// Общие константы модулей Overview (после перехода на overview2 остался только
// гендерный стиль). Цвета ♂/♀ — отдельная семантика, по образцу row-male/female,
// НЕ смешивать с цветами модулей --theme-module-*.

export const GENDER_CIRCLE: Record<'male' | 'female', React.CSSProperties> = {
  male: { background: 'rgba(29,78,216,.12)', color: '#1d4ed8' },
  female: { background: 'rgba(190,24,93,.12)', color: '#be185d' },
};
