import React from 'react';
import {
  RK_STROKES, distanceLabel, strokeByKey, type RkFilters,
} from '../rk-disciplines';

/**
 * Выбор дисциплины: бассейн, пол, стиль, дистанция.
 *
 * Полосой кнопок, а не селектами: осей всего четыре, значений у каждой единицы, и весь
 * выбор виден целиком — сравнивать 50 и 100 метров ходят в один тап, а не через выпадающий
 * список. Эстафеты отделены в свой ряд: «4×100m» рядом с «100m» читается как одна лестница,
 * хотя это разные соревнования.
 */

interface Props {
  filters: RkFilters;
  onChange: (patch: Partial<RkFilters>) => void;
  /**
   * Показывать ли стиль и дистанцию. Табу «World records» они не нужны: он показывает все
   * дисциплины сразу, и выбор дистанции там ничего бы не значил.
   */
  showEvent?: boolean;
  /**
   * Показывать ли эстафеты. У мастерсов их нет ни в одной оси: в мировых мастерских мы их
   * не берём (там полоса — СУММА возрастов четвёрки, другая ось), в израильских их нет.
   */
  allowRelays?: boolean;
}

const POOLS: Array<{ key: '25m' | '50m'; label: string }> = [
  { key: '50m', label: '50m pool' },
  { key: '25m', label: '25m pool' },
];

const GENDERS: Array<{ key: 'male' | 'female'; label: string }> = [
  { key: 'male', label: 'Men' },
  { key: 'female', label: 'Women' },
];

const RkDisciplinePicker: React.FC<Props> = ({
  filters, onChange, showEvent = true, allowRelays = true,
}) => {
  const stroke = strokeByKey(filters.stroke);
  const distances = stroke?.distances ?? [];
  const relays = allowRelays ? (stroke?.relays ?? []) : [];

  /**
   * Смена стиля может оставить дистанцию, которой у нового стиля нет (800 вольным → спина).
   * Тогда берём первую доступную: пустая таблица без объяснения читается как «данных нет».
   */
  const pickStroke = (key: string) => {
    const next = strokeByKey(key);
    const keep = next && filters.distance
      && [...next.distances, ...(allowRelays ? next.relays : [])].includes(filters.distance);
    onChange({ stroke: key, distance: keep ? filters.distance : (next?.distances[0] ?? null) });
  };

  return (
    <div className="rk-picker">
      <div className="rk-picker__row">
        <span className="rk-picker__label">Pool</span>
        <div className="rk-chips">
          {POOLS.map((p) => (
            <button
              key={p.key}
              type="button"
              className={`rk-chip${filters.poolType === p.key ? ' rk-chip--on' : ''}`}
              aria-pressed={filters.poolType === p.key}
              onClick={() => onChange({ poolType: p.key })}
            >
              {p.label}
            </button>
          ))}
        </div>
      </div>

      <div className="rk-picker__row">
        <span className="rk-picker__label">Gender</span>
        <div className="rk-chips">
          {GENDERS.map((g) => (
            <button
              key={g.key}
              type="button"
              className={`rk-chip${filters.gender === g.key ? ' rk-chip--on' : ''}`}
              aria-pressed={filters.gender === g.key}
              onClick={() => onChange({ gender: g.key })}
            >
              {g.label}
            </button>
          ))}
        </div>
      </div>

      {showEvent && (
        <>
        <div className="rk-picker__row">
          <span className="rk-picker__label">Stroke</span>
          <div className="rk-chips">
            {RK_STROKES.map((s) => (
              <button
                key={s.key}
                type="button"
                className={`rk-chip${filters.stroke === s.key ? ' rk-chip--on' : ''}`}
                aria-pressed={filters.stroke === s.key}
                onClick={() => pickStroke(s.key)}
              >
                {s.label}
              </button>
            ))}
          </div>
        </div>

        <div className="rk-picker__row">
          <span className="rk-picker__label">Distance</span>
          <div className="rk-chips">
            {distances.map((d) => (
              <button
                key={d}
                type="button"
                className={`rk-chip${filters.distance === d ? ' rk-chip--on' : ''}`}
                aria-pressed={filters.distance === d}
                onClick={() => onChange({ distance: d })}
              >
                {distanceLabel(d)}
              </button>
            ))}
            {relays.length > 0 && (
              <>
                <span className="rk-chips__sep" aria-hidden="true" />
                {relays.map((d) => (
                  <button
                    key={d}
                    type="button"
                    className={`rk-chip rk-chip--relay${filters.distance === d ? ' rk-chip--on' : ''}`}
                    aria-pressed={filters.distance === d}
                    onClick={() => onChange({ distance: d })}
                    title="Relay"
                  >
                    {distanceLabel(d)}
                  </button>
                ))}
              </>
            )}
          </div>
        </div>
        </>
      )}
    </div>
  );
};

export default RkDisciplinePicker;
