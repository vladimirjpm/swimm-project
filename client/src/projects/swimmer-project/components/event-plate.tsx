import React from 'react';
import UI_PoolIcon from '../../components/mix/pool-icon/pool-icon';
import UI_SwimmStyleIcon from '../../components/mix/swimm-style-icon/swimm-style-icon';

/**
 * Плита события: иконка стиля + дистанция + бассейн. ОДИН блок на всю страницу спортсмена —
 * строка результата, таблица Season best и таблица Records показывают событие одинаково.
 *
 * Отдельным модулем, а не внутри swimmer-panels: панели импортируют строку результата, и
 * держать плиту в панелях означало бы круговой импорт.
 *
 * Дистанцию и «--25m--» рисуют сами компоненты (`UI_SwimmStyleIcon`, `UI_PoolIcon`), а белая
 * подложка — CSS страницы: PNG иконок нарисованы под светлый фон и в тёмной теме без плиты
 * пропадают. Ровно поэтому здесь `icon-notext` + свой `__dist`, а не готовый `icon-len`:
 * у него дистанция красная по прозрачному, и на тёмной теме её не видно.
 *
 * `title` обязателен: плита картиночная, и без него читаемого текста в ней не остаётся.
 */

/** «individual_medley» → «individual medley»: ключ стиля приходит машинным. */
export const strokeLabel = (stroke?: string | null) => (stroke ?? '').replace(/_/g, ' ');

interface Props {
  stroke?: string | null;
  distance: string;
  poolType: string;
}

function EventPlate({ stroke, distance, poolType }: Props) {
  return (
    <div className="deep-event-plate" title={`${distance} ${strokeLabel(stroke)} · ${poolType}`}>
      <div className="deep-stroke-plate">
        <UI_SwimmStyleIcon styleName={stroke ?? ''} styleType="icon-notext" />
        <span className="deep-stroke-plate__dist">{distance}</span>
      </div>
      <UI_PoolIcon
        styleType="icon-text-center"
        label={poolType}
        labelClassName="deep-event-plate__pool"
      />
    </div>
  );
}

export default EventPlate;
