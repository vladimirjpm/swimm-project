import React from 'react';

/**
 * Нагрузка строки на листе печати (§3.2): чипы «V1 V2 V3» КОНТУРОМ — чёрный текст, рамка, без
 * заливки. Свой компонент, а не `UI_IntensityIcon`: тот цветной и открывает шкалу по нажатию.
 * Не по шкале — текстом. Ключи уже разобраны моделью (`SheetLine.chips` / `intensityText`).
 *
 * Вид листа выбирает классы: у `large` — `wpl-*` (по умолчанию), у листа кубов — свои
 * (`chipClassName`, `className`). Один чип — `PrintChip`: им же виды рисуют уровень, найденный в
 * тексте тренера («4V» → [V4], хендофф 27.11 §5), и слово главного сета в рамке («מרכזי», §4 — с `dir`).
 */
export const PrintChip: React.FC<{ label: string; className?: string; dir?: 'ltr' | 'rtl' }> = ({
  label, className = 'wpl-chip', dir = 'ltr',
}) => (
  <bdi dir={dir} className={className}>{label}</bdi>
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
