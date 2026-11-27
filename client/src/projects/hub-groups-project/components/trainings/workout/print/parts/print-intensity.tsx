import React from 'react';

/**
 * Нагрузка строки на листе печати (§3.2): чипы «V1 V2 V3» КОНТУРОМ — чёрный текст, рамка, без
 * заливки. Свой компонент, а не `UI_IntensityIcon`: тот цветной и открывает шкалу по нажатию.
 * Не по шкале — текстом. Ключи уже разобраны моделью (`SheetLine.chips` / `intensityText`).
 *
 * Вид листа выбирает классы: у `large` — `wpl-*` (по умолчанию), у листа строк — свои
 * (`chipClassName`, `className`). Один чип — `PrintChip`: им же лист строк рисует уровень,
 * найденный в тексте тренера («4V» → [V4], хендофф 27.11 §5).
 */
export const PrintChip: React.FC<{ label: string; className?: string }> = ({ label, className = 'wpl-chip' }) => (
  <bdi dir="ltr" className={className}>{label}</bdi>
);

export const PrintIntensity: React.FC<{
  chips: string[] | null;
  text: string | null;
  className?: string;
  chipClassName?: string;
  textClassName?: string;
}> = ({ chips, text, className = 'wpl-chips', chipClassName = 'wpl-chip', textClassName = 'wpl-intensity-text' }) => {
  if (chips) {
    return (
      <bdi dir="ltr" className={className}>
        {chips.map((c) => <span key={c} className={chipClassName}>{c}</span>)}
      </bdi>
    );
  }
  if (text) return <bdi dir="ltr" className={textClassName}>{text}</bdi>;
  return null;
};
