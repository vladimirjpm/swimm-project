import React from 'react';

/**
 * Чип «Partial results» у соревнования: протокол затянут, а части событий в нём ещё нет
 * (непроплыты или федерация не выложила). Решение Р114 (docs/data-integrity.md): отметку
 * ставит импорт по разобранному файлу, снимает повторный импорт полного протокола или админ
 * («Принять как есть»). Сервер отдаёт её полем `partial_results` — текстом подсказки
 * («Waiting for 2 of 16 events: …»); null — чипа нет. Клиент ничего не решает сам.
 *
 * Один компонент на все места: шапка соревнования, селектор соревнований, календарь
 * `/competitions`. Подпись одна, подсказка — из данных.
 *
 * Цвета — токены спорного времени (`--theme-flag-*`, как у чипа «Under review»): это та же
 * интонация «смотри внимательнее». Вариант `hero` — для шапки на фоне `--theme-primary`:
 * там янтарь нечитаем, чип красится парным токеном текста шапки, как бейдж «N Days».
 */

export const PARTIAL_RESULTS_LABEL = 'Partial results';

/**
 * Поверхность, на которой стоит чип:
 * - `flag` — поверхность режима (`--theme-mode-*`, селектор соревнований): токены `--theme-flag-*`;
 * - `hero` — шапка на `--theme-primary`: парный токен `--theme-mode-accent-text`;
 * - `deep` — страницы темы deep (календарь `/competitions`): роли `--t-warn*`.
 */
export type PartialResultsChipVariant = 'flag' | 'hero' | 'deep';

const VARIANT_STYLE: Record<PartialResultsChipVariant, React.CSSProperties> = {
  flag: {
    background: 'var(--theme-flag-chip-bg)',
    color: 'var(--theme-flag-text)',
    borderColor: 'var(--theme-flag-chip-border)',
  },
  hero: {
    borderColor: 'color-mix(in srgb, var(--theme-mode-accent-text) 40%, transparent)',
    background: 'color-mix(in srgb, var(--theme-mode-accent-text) 15%, transparent)',
    color: 'inherit',
  },
  deep: {
    background: 'var(--t-warn-soft)',
    color: 'var(--t-warn)',
    borderColor: 'var(--t-warn-border)',
  },
};

interface Props {
  /** Чего ещё нет — `partial_results` из API. */
  hint: string;
  size?: 'md' | 'sm';
  variant?: PartialResultsChipVariant;
  className?: string;
}

const UI_PartialResultsChip: React.FC<Props> = ({ hint, size = 'md', variant = 'flag', className = '' }) => {
  const style = VARIANT_STYLE[variant];
  const title = `Results are not complete yet. ${hint}`;
  return (
    <span
      className={`inline-flex shrink-0 items-center gap-1 whitespace-nowrap rounded-full border font-extrabold leading-none ${
        size === 'sm' ? 'px-1.5 py-[2px] text-[9.5px]' : 'px-2 py-[3px] text-[11px]'
      } ${className}`}
      style={style}
      title={title}
      aria-label={`${PARTIAL_RESULTS_LABEL}. ${title}`}
    >
      <span aria-hidden="true">⏳</span>
      {PARTIAL_RESULTS_LABEL}
    </span>
  );
};

export default UI_PartialResultsChip;
