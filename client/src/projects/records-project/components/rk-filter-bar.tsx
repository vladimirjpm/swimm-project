import React from 'react';
import FilterBar, { type FilterBarChip } from '../../components/filter-section/filter-bar';
import UI_SwimmStyleIcon from '../../components/mix/swimm-style-icon/swimm-style-icon';
import UI_PoolIcon from '../../components/mix/pool-icon/pool-icon';
import { strokeByKey, type RkFilters } from '../rk-disciplines';

/**
 * Полоса выбранного на `/records` — ТОТ ЖЕ общий `FilterBar`, что на results, `/season-best`
 * и `/my-media` (Ф1, `docs/ui-components.md` §6). Своей вёрстки полосы здесь нет и быть не
 * должно: это был бы четвёртый дубль одного и того же.
 *
 * Зачем она на странице, где фильтры и так стоят чипами прямо над ней (решение Влада
 * 20.09.2026): на табе Masters стиль и дистанция ОБЩИЕ для всей таблицы, и карточка рекорда
 * рисуется без своей шапки. Вопрос «какой это заплыв» отвечает полоса — один раз сверху,
 * а не иконкой над каждой из шестнадцати возрастных полос.
 *
 * Чипы здесь всегда выбраны, кроме Age group: у дисциплины пол, бассейн, стиль и дистанция
 * обязаны иметь значение — «все стили сразу» страница не показывает.
 *
 * `ageBand` — таб World Junior: там возраст не выбирают, он задан самим эталоном (WJR один
 * на полосу ж 14–17 / м 15–18). Чип тогда называется «Age band» и всегда выбран — это
 * главная оговорка экрана, а не фильтр.
 */
const RkFilterBar: React.FC<{ filters: RkFilters; className?: string; ageBand?: string | null }> = ({
  filters, className, ageBand,
}) => {
  const age = ageBand ? ageBand.replace('-', '–') : filters.ageGroup;
  const strokeLabel = strokeByKey(filters.stroke)?.label ?? filters.stroke ?? '';
  // Иконка несёт дистанцию сама — подпись стиля рядом с ней была бы тем же самым словом
  // дважды. Ширины те же, что у чипа Event на results.
  //
  // ⚠ «m» с дистанции снимаем: справочник дисциплин пишет «50m», а плита события во всём
  // продукте подписана формой протокола — «50». На странице пловца ту же нормализацию
  // делает сервер (`SwimmersPublicController`, `Distance.TrimEnd('m','M')`), и без неё один
  // и тот же чип был бы подписан на двух страницах по-разному (поймано Владом 20.09.2026).
  // Буква не теряется: она осталась в подписи полосы («Freestyle 50m») и в чипах пикера.
  const event = (width: string, text: string, len: number) => (
    <div className={`${width} [&_img]:w-full [&_img]:h-auto`}>
      <UI_SwimmStyleIcon
        styleName={filters.stroke ?? ''}
        styleLen={(filters.distance ?? '').replace(/m$/i, '')}
        styleType="icon-len"
        // Дистанция ПОД стилем, как в карточке рекорда под ней: числом поверх рисунка она
        // спорит с ним, а тут это главная подпись чипа (просьба Влада 20.09.2026).
        lenPlacement="below"
        lenSize={len}
        className={`src-rk-filter-bar font-bold ${text}`}
      />
    </div>
  );

  const chips: FilterBarChip[] = [
    {
      key: 'event',
      label: 'Event',
      active: true,
      value: event('w-[96px]', 'text-base', 22),
      valueCompact: event('w-[52px]', 'text-[13px]', 14),
    },
    {
      key: 'gender',
      label: 'Gender',
      active: true,
      value: (
        <span className="text-2xl font-extrabold leading-none text-[var(--deep-text)]">
          {filters.gender === 'female' ? 'Women' : 'Men'}
        </span>
      ),
      valueCompact: (
        <span className="text-[16px] font-extrabold leading-[1.2] text-[var(--deep-accent)]">
          {filters.gender === 'female' ? 'Women' : 'Men'}
        </span>
      ),
    },
    {
      key: 'age',
      label: ageBand ? 'Age band' : 'Age group',
      shortLabel: 'Age',
      // Единственный чип, который бывает невыбранным: «All» здесь осмысленно — показаны все
      // полосы сразу, и именно так таб открывается.
      active: !!age,
      value: (
        <span className="text-2xl font-extrabold leading-none text-[var(--deep-text)]">
          {age}
        </span>
      ),
      valueCompact: (
        <span className="text-[16px] font-extrabold leading-[1.2] text-[var(--deep-accent)]">
          {age}
        </span>
      ),
    },
    {
      key: 'pool',
      label: 'Pool',
      active: true,
      value: (
        <UI_PoolIcon styleType="icon-text-top" label={filters.poolType ?? ''} iconWidth="40" labelClassName="text-sm" />
      ),
      valueCompact: (
        <UI_PoolIcon styleType="icon-text-top" label={filters.poolType ?? ''} iconWidth="26" labelClassName="text-[11px]" />
      ),
    },
  ];

  return (
    <FilterBar
      chips={chips}
      desktop="columns"
      rows="card"
      className={className}
      aside={<span className="rk-fb__aside">{strokeLabel} {filters.distance}</span>}
    />
  );
};

export default RkFilterBar;
