import React from 'react';
import './filter-bar.css';

/**
 * ПОЛОСА ВЫБРАННЫХ ФИЛЬТРОВ — одна на продукт (Ф1 плана
 * `docs/plans/my-media-filters-plan.md`).
 *
 * До этого её писали дважды: `ResultsFilteredInfo` на results и `sb-filter-bar` на
 * `/season-best` — с одинаковыми правилами и разными палитрами. Это был последний
 * незакрытый дубль панели фильтров (`docs/ui-components.md` §6,
 * `docs/plans/filters-reusable-panel-plan.md` §4 п.4).
 *
 * Правила, ради которых компонент и существует (терять их нельзя):
 *  • чип в состоянии «All» не занимает полноценное место — он мельче и приглушён;
 *  • двухстрочный вид: сверху мелко всё «All», снизу крупно и по центру выбранное;
 *  • пустая строка не рендерится вовсе — её нет, а не «есть, но пустая».
 *
 * Компонент рисует полосу; ЧТО в ней написано, решает страница — она собирает `chips`.
 * Никакой логики фильтров здесь нет и быть не должно.
 */

export interface FilterBarChip {
  /** Ключ фильтра. Уезжает в класс `fb__chip--<key>`, чтобы страница могла дотянуться. */
  key: string;
  label: string;
  /** Подпись для узкой верхней строки. Не задана — печатается `label`. */
  shortLabel?: string;
  /** Фильтр выбран (значение сужает выборку). */
  active: boolean;
  /** Что печатать в выбранном состоянии. */
  value: React.ReactNode;
  /**
   * То же значение для двухстрочного вида. Нужно там, где вид зависит от ширины: на results
   * двухстрочный вид — мобильный, и иконки в нём мельче. Не задано — печатается `value`
   * (так у `/season-best`, где двухстрочный вид единственный).
   */
  valueCompact?: React.ReactNode;
  /** Что печатать в состоянии «не выбрано». По умолчанию «All». */
  idleValue?: React.ReactNode;
  /**
   * Довесок под значением в состоянии «не выбрано» — ТОЛЬКО в раскладке `columns`.
   * Заведён под индикатор `[prelim]` у Date на results: он поясняет не выбор, а данные,
   * и в тесной верхней строке мобайла ему места нет.
   */
  idleExtra?: React.ReactNode;
  /** Не печатать подпись над значением (чип Event на `/season-best`: место отдано картинке). */
  hideLabel?: boolean;
  /** Тон выбранного: акцент темы или золото (подиум). */
  tone?: 'accent' | 'gold';
  /** Клик по чипу. Задан — чип нажимаемый и в раскладке `columns` получает подложку тона. */
  onClick?: () => void;
  /**
   * В раскладке `columns` не рендерить чип, пока он не выбран. Для Position на results:
   * у остальных «All» означает осмысленное «в выборке все значения», а у него — просто
   * выключенный фильтр.
   */
  hideWhenIdle?: boolean;
}

interface Props {
  chips: FilterBarChip[];
  /**
   * Ведущая ячейка — то, что стоит В полосе, но чипом не является: сезон на `/my-media`
   * (он не фильтр общей модели, а другой запрос к серверу, и переключается своим шагом).
   * В раскладке `columns` встаёт первой колонкой с разделителем, в двухстрочной — отдельной
   * строкой над «All».
   */
  lead?: React.ReactNode;
  /**
   * Приписка справа от ведущей ячейки в двухстрочном виде (на макете это «4 swims · date ↓»).
   * В раскладке `columns` НЕ рисуется: там та же строка живёт под полосой, и печатает её
   * страница — полосе про счётчик знать незачем.
   */
  aside?: React.ReactNode;
  /**
   * Как выглядит полоса на ≥768px: `columns` — одна полоса равных колонок с разделителями
   * (results), `rows` — те же две строки, что на мобайле (`/season-best`, решение Влада
   * 2026-08-26: фильтров много, и равные колонки размазывали выбранное между пустыми «All»).
   */
  desktop?: 'columns' | 'rows';
  /**
   * Геометрия двухстрочного вида. `card` — на подложке, неактивные чипы тянутся во всю
   * ширину, между строками пунктир (results); `bare` — без подложки, чипы по содержимому
   * (`/season-best`). Две сложившиеся картинки; свести их в одну — отдельное решение,
   * а не побочный эффект выноса компонента.
   */
  rows?: 'card' | 'bare';
  /** Класс корня: внешние отступы и переопределение токенов `--fb-*` — дело страницы. */
  className?: string;
}

const toneClass = (chip: FilterBarChip) =>
  chip.tone === 'gold' ? 'fb__tone--gold' : 'fb__tone--accent';

function FilterBar({ chips, lead, aside, desktop = 'columns', rows = 'card', className }: Props) {
  const idle = chips.filter((c) => !c.active);
  const active = chips.filter((c) => c.active);
  // `hideWhenIdle` действует только на колонки: в двухстрочном виде невыбранный чип живёт
  // в верхней строке — она для этого и заведена.
  const columnChips = chips.filter((c) => c.active || !c.hideWhenIdle);

  return (
    <div
      className={`fb fb--${rows}${desktop === 'columns' ? ' fb--cols' : ''}${
        className ? ` ${className}` : ''
      }`}
    >
      {desktop === 'columns' && (
        <div className="fb__cols">
          {lead && (
            <div className="fb__cell fb__cell--lead fb__cell--divided">{lead}</div>
          )}
          {columnChips.map((chip, i) => (
            // Разделитель — на обёртке, а не на самой колонке: так подложка выбранного
            // (скруглённый фон) не съедает вертикальную линию и не ломает ритм полосы.
            <div
              key={chip.key}
              className={
                `fb__cell${chip.active ? ' fb__cell--active' : ''}` +
                (i === columnChips.length - 1 ? '' : ' fb__cell--divided')
              }
            >
              <div
                onClick={chip.onClick}
                className={
                  `fb__col fb__col--${chip.key}` +
                  (chip.onClick ? ' fb__col--click' : '') +
                  (chip.active && chip.onClick ? ` fb__col--tone ${toneClass(chip)}` : '')
                }
              >
                <span className="fb__label">{chip.label}</span>
                {chip.active ? (
                  chip.value
                ) : (
                  <span className="fb__col-value--idle">{chip.idleValue ?? 'All'}</span>
                )}
                {!chip.active && chip.idleExtra}
              </div>
            </div>
          ))}
        </div>
      )}

      <div className="fb__rows">
        {(lead || aside) && (
          <div className="fb__row fb__row--lead">
            {lead}
            {aside && <span className="fb__aside">{aside}</span>}
          </div>
        )}
        {idle.length > 0 && (
          <div className="fb__row fb__row--idle">
            {idle.map((chip) => (
              <div
                key={chip.key}
                onClick={chip.onClick}
                className={
                  `fb__chip fb__chip--idle fb__chip--${chip.key}` +
                  (chip.onClick ? ' fb__chip--click' : '')
                }
              >
                <span className="fb__label fb__label--idle">{chip.shortLabel ?? chip.label}</span>
                <span className="fb__chip-value">{chip.idleValue ?? 'All'}</span>
              </div>
            ))}
          </div>
        )}
        {active.length > 0 && (
          <div
            className={`fb__row fb__row--active${idle.length > 0 ? ' fb__row--divided' : ''}`}
          >
            {active.map((chip) => (
              <div
                key={chip.key}
                onClick={chip.onClick}
                className={
                  `fb__chip fb__chip--active fb__chip--${chip.key} ${toneClass(chip)}` +
                  (chip.onClick ? ' fb__chip--click' : '')
                }
              >
                {!chip.hideLabel && <span className="fb__label">{chip.label}</span>}
                <span className="fb__chip-value">{chip.valueCompact ?? chip.value}</span>
              </div>
            ))}
          </div>
        )}
      </div>
    </div>
  );
}

export default FilterBar;
