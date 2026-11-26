import React from 'react';

/**
 * Нагрузка строки на листе печати (§3.2): чипы «V1 V2 V3» КОНТУРОМ — чёрный текст, рамка 0.08em,
 * без заливки. Свой компонент, а не `UI_IntensityIcon`: тот цветной и открывает шкалу по нажатию.
 * Не по шкале — текстом. Ключи уже разобраны моделью (`SheetLine.chips` / `intensityText`).
 */
export const PrintIntensity: React.FC<{ chips: string[] | null; text: string | null }> = ({ chips, text }) => {
  if (chips) {
    return (
      <bdi dir="ltr" className="wpl-chips">
        {chips.map((c) => <span key={c} className="wpl-chip">{c}</span>)}
      </bdi>
    );
  }
  if (text) return <bdi dir="ltr" className="wpl-intensity-text">{text}</bdi>;
  return null;
};
