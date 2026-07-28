import React from 'react';

interface UI_AgeLabelProps {
  age: string | number;
  isMasters?: boolean;
  ageGroup?: string;
  /**
   * Категория заплыва из протокола: 'open' | 'para' | 'mix' | 'mix-17' | '17' | '25-29'…
   * Бейджем показывается только программа — см. categoryBadge.
   */
  eventCategory?: string | null;
  className?: string;
}

/**
 * Возрастные категории («17», «25-29») бейджем НЕ показываем: они дублируют возраст, который
 * стоит рядом, и превращают таблицу в шум. Показываем только то, что из возраста не выводится, —
 * отдельную программу заплыва.
 *
 * Зачем: в одной дисциплине бывает несколько первых мест, потому что это разные программы
 * (у Маккабиады 50 вольным у мужчин — «Men», «U17 Boys» и «Men Para»). Без подписи это
 * выглядит как ошибка данных.
 */
function categoryBadge(eventCategory?: string | null): string | null {
  if (!eventCategory) return null;
  const v = eventCategory.toLowerCase();
  if (v === 'para') return 'PARA';
  if (v === 'mix' || v.startsWith('mix-')) return 'MIX';
  return null;
}

const UI_AgeLabel: React.FC<UI_AgeLabelProps> = ({
  age,
  isMasters = false,
  ageGroup,
  eventCategory,
  className = '',
}) => {
  const badge = categoryBadge(eventCategory);

  return (
    <div className={`flex flex-col ${className}`}>
      <div className="font-bold text-sm mt-1">
        <span className="font-normal text-xs">age:</span> {age}
        {badge && (
          <span
            className="ml-1 align-middle inline-block rounded px-1 text-[9px] font-bold tracking-wide
                       bg-[var(--theme-primary)] text-[var(--theme-mode-accent-text)]"
            title="Separate programme — places and medals are counted within it"
          >
            {badge}
          </span>
        )}
      </div>
      {isMasters && ageGroup && (
        <div className="font-normal text-xs text-red-700">
          [{ageGroup}]
        </div>
      )}
    </div>
  );
};

export default UI_AgeLabel;
