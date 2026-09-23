import React from 'react';
import '../../components/mix/h2h/h2h.css';
import UI_FlagEmoji from '../../components/mix/flag-icon/flag-icon';
import type { RecordCountryOption } from '../../../hooks/useRecordsCompare';

/**
 * Выбор страны для `/records/compare` — тот же приём, что выбор соперника на `/h2h`
 * (`UI_H2HRivalPicker`): полоса быстрых кнопок + поиск, ОДИН на обе стороны, в потоке
 * под слотами, а не поповером.
 *
 * Пикер один, потому что два селекта рядом читались как два разных выбора, хотя
 * выбирают они одно и то же; какую сторону заполнит клик — говорит подпись над
 * пикером и подсвеченный слот.
 *
 * Своя реализация, а не `UI_H2HRivalPicker`: тот ищет пловцов на сервере
 * (`/api/swimmers/search`, «наберите две буквы»), а страны приезжают ВСЕ одним
 * списком `/api/records/countries` и фильтруются на месте — ждать сервер, имея данные
 * в руках, значило бы сделать выбор медленнее без причины. Классы взяты его же, чтобы
 * оба пикера продукта выглядели одинаково.
 */
interface Props {
  countries: RecordCountryOption[];
  /** Уже занятые стороны — из выдачи убираются: сравнения страны с собой не бывает. */
  taken: Array<string | null>;
  query: string;
  onQuery: (q: string) => void;
  onPick: (code: string) => void;
  /**
   * Быстрые кнопки над поиском: домашняя страна и те, чьи пловцы держат больше всего
   * ДЕЙСТВУЮЩИХ мировых рекордов (порядок считает вызывающий). Подпись называет ось
   * прямо — «Top nations» читалась как оценка силы, и её опровергал первый же взгляд:
   * Россия с тремя рекордами не влезала, Германия с четырьмя влезала (вопрос Влада
   * 23.09.2026).
   */
  quick: string[];
  inputRef?: React.Ref<HTMLInputElement>;
}

const RcCountryPicker: React.FC<Props> = ({
  countries, taken, query, onQuery, onPick, quick, inputRef,
}) => {
  const q = query.trim().toUpperCase();
  const free = countries.filter((c) => !taken.includes(c.code));
  // Выдача раскрывается ТОЛЬКО на запрос — как поиск соперника в H2H. Стран с рекордами
  // больше двух сотен, и список «на всякий случай» отодвигал бы само сравнение за нижний
  // край экрана; кто не печатает, берёт страну из кнопок над поиском.
  const hits = q ? free.filter((c) => c.code.includes(q)) : [];
  const quickFree = quick.filter((code) => !taken.includes(code));

  return (
    <div className="h2h-picker">
      {quickFree.length > 0 && (
        <div className="h2h-picker__favs">
          <span className="h2h-picker__cap">Most world records</span>
          {quickFree.map((code) => (
            <button key={code} type="button" className="h2h-fav-chip" onClick={() => onPick(code)}>
              <UI_FlagEmoji countryCode={code} size="16x12" className="src-rc-country-picker" />
              <span>{code}</span>
            </button>
          ))}
        </div>
      )}

      <input
        ref={inputRef}
        className="h2h-search"
        type="search"
        value={query}
        onChange={(e) => onQuery(e.target.value)}
        placeholder="Search a country code to compare with..."
        aria-label="Search a country to compare with"
      />

      {q && (
        <div className="h2h-hits">
          {countries.length === 0 ? (
            <div className="h2h-hint">Loading countries…</div>
          ) : hits.length === 0 ? (
            <div className="h2h-hint">No country with that code.</div>
          ) : (
            hits.map((c) => (
              <button key={c.code} type="button" className="h2h-hit" onClick={() => onPick(c.code)}>
                <UI_FlagEmoji countryCode={c.code} size="24x18" className="rc-hit__flag src-rc-country-picker" />
                <span className="h2h-hit__name">{c.code}</span>
                <span className="h2h-hit__meta">
                  {c.records} {c.records === 1 ? 'record' : 'records'}
                </span>
              </button>
            ))
          )}
        </div>
      )}
    </div>
  );
};

export default RcCountryPicker;
