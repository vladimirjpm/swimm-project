import React from 'react';

/**
 * ЕДИНСТВЕННОЕ место, где живёт пометка [prelim] (цвет, текст, тултип) — по тому же
 * принципу, что UI_SwimTime: признак должен выглядеть одинаково везде, где показан
 * заплыв, и перечислением мест это не удержать.
 *
 * Рендерит null, если заплыв не предварительный, поэтому вызывающему не нужен свой
 * if: `<UI_PrelimLabel heatType={res.heat_type} />` безопасен на любой строке
 * (timed-финалы и старые данные без признака ничего не показывают).
 */
export const PRELIM_COLOR = '#f59e0b';
export const PRELIM_ON_COLOR = '#22c55e';

interface Props {
  /** 'prelim' | 'final' | null/undefined — рендер только для 'prelim' (если нет state). */
  heatType?: string | null;
  /** Размер/раскладка по месту вызова; цвет и жирность не переопределять. */
  className?: string;
  /** Тултип; дефолт — про медали в финале (для сводки фильтров текст другой). */
  title?: string;
  /** Режим индикатора (не строка результата, heatType игнорируется):
   *  'on' → зелёный [prelim ON] (прелимы показаны), 'off' → оранжевый [prelim OFF],
   *  'has' → оранжевый [has prelim] — «в соревновании есть прелимы» (кнопка фильтра). */
  state?: 'on' | 'off' | 'has';
}

const UI_PrelimLabel: React.FC<Props> = ({ heatType, className, title, state }) => {
  if (state) {
    const text = state === 'on' ? '[prelim ON]' : state === 'off' ? '[prelim OFF]' : '[has prelim]';
    const tip = state === 'on'
      ? 'Preliminary heats are shown'
      : state === 'off'
        ? 'Preliminary heats are hidden — toggle [prelim] in the Date filter'
        : 'This competition has preliminary heats — click to show them';
    return (
      <span
        className={`font-bold uppercase tracking-wide leading-none ${className ?? 'text-[9px]'}`}
        style={{ color: state === 'on' ? PRELIM_ON_COLOR : PRELIM_COLOR }}
        title={title ?? tip}
      >
        {text}
      </span>
    );
  }
  return heatType !== 'prelim' ? null : (
    <span
      className={`font-bold uppercase tracking-wide leading-none ${className ?? 'text-[9px]'}`}
      style={{ color: PRELIM_COLOR }}
      title={title ?? 'Preliminary heat — medals are awarded in the final'}
    >
      [prelim]
    </span>
  );
};

export default UI_PrelimLabel;
