import React from 'react';

/**
 * ЕДИНСТВЕННОЕ место, где живёт пометка раунда зачёта — по тому же принципу, что
 * UI_SwimTime и UI_PrelimLabel: признак обязан выглядеть одинаково везде, где показан заплыв.
 *
 * Зачем вообще. У чемпионата формата «мокдамот и финал» утренние заплывы по возрастным
 * группам и вечерний финал — ДВА самостоятельных зачёта: медали и клубные очки дают в
 * обоих. Поэтому один пловец законно занимает ПЕРВОЕ место дважды, и без метки строки
 * выглядят как задвоение (docs/data-integrity.md §10).
 *
 * Рендерит null, когда раунда нет (обычные соревнования, старые данные) и для 'prelim' —
 * им занимается UI_PrelimLabel со своим смыслом «место не награда».
 */
export const ROUND_COLOR = '#38bdf8';

interface Props {
  /** 'timed-final' | 'final' | 'prelim' | null/undefined. */
  round?: string | null;
  /** Размер/раскладка по месту вызова; цвет и жирность не переопределять. */
  className?: string;
}

const LABELS: Record<string, { text: string; title: string }> = {
  'timed-final': {
    text: '[age final]',
    title: 'Age-group final — its own medals and club points, separate from the championship final',
  },
  final: {
    text: '[final]',
    title: 'Championship final — swum across age groups, scored on its own',
  },
};

/**
 * Порядок раундов в списке результатов: сперва утренний зачёт возрастных групп, затем
 * финал первенства, затем предварительные (они и так скрыты по умолчанию). Строки без
 * раунда (обычные соревнования, эстафеты) идут своим блоком последними, и на них
 * разбиения не видно — блок ровно один.
 */
export const ROUND_ORDER: Record<string, number> = {
  'timed-final': 0,
  final: 1,
  prelim: 2,
};

/** Заголовок блока раунда в списке результатов; null — раунда нет, блок не подписываем. */
export const roundSectionTitle = (round?: string | null): string | null =>
  round === 'timed-final' ? 'Age-group finals'
    : round === 'final' ? 'Championship final'
      : round === 'prelim' ? 'Preliminary heats'
        : null;

const UI_RoundLabel: React.FC<Props> = ({ round, className }) => {
  const label = round ? LABELS[round] : undefined;
  if (!label) return null;

  return (
    <span
      className={`font-bold uppercase tracking-wide leading-none ${className ?? 'text-[9px]'}`}
      style={{ color: ROUND_COLOR }}
      title={label.title}
    >
      {label.text}
    </span>
  );
};

export default UI_RoundLabel;
