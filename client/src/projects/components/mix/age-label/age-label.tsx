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
  /** Эстафета: только у неё «mix» означает смешанный по полу заплыв. */
  isRelay?: boolean;
  /** Пол строки: 'male' | 'female' | 'none' | пусто. */
  gender?: string | null;
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
 *
 * ⚠ MIX — ТОЛЬКО У ЭСТАФЕТ (решение Влада 2026-08-04). В плавании «mixed» означает
 * смешанный по полу состав, и это осмысленно лишь для эстафеты. У личных заплывов протокол
 * тоже пишет «מעורב» (`mix-10-16`, `mix-shabbat`), но там это способ собрать заплыв, а не
 * программа: результаты всё равно идут в зачёт по возрастам и фильтруются по ним. Бейдж там
 * не объяснял ничего и вдобавок читался как «смешанный пол» — 100 брассом у девочек
 * (событие 18) выглядело смешанным заплывом, хотя это обычный женский зачёт.
 */
function categoryBadge(eventCategory?: string | null, isRelay?: boolean): string | null {
  if (!eventCategory) return null;
  const v = eventCategory.toLowerCase();
  if (v === 'para') return 'PARA';
  if (isRelay && (v === 'mix' || v.startsWith('mix-'))) return 'MIX';
  return null;
}

const UI_AgeLabel: React.FC<UI_AgeLabelProps> = ({
  age,
  isMasters = false,
  ageGroup,
  eventCategory,
  isRelay = false,
  gender,
  className = '',
}) => {
  const badge = categoryBadge(eventCategory, isRelay);
  // Пол неизвестен — это дыра в данных, а не программа: в смешанном заплыве протокол не даёт
  // пола в шапке, а у пловца он не заполнен. Показываем именно её, иначе строка молча
  // выпадает из половых зачётов и рекордов (проверка results.no-gender).
  const genderUnknown = !isRelay && gender !== 'male' && gender !== 'female';

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
        {genderUnknown && (
          <span
            className="ml-1 align-middle inline-block rounded px-1 text-[9px] font-bold tracking-wide
                       border border-current text-amber-600 dark:text-amber-400"
            title="Gender is missing in the protocol — this swim is left out of gender standings and records"
          >
            ?
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
