import React from 'react';
import '../../components/mix/h2h/h2h.css';
import UI_H2HStatRow from '../../components/mix/h2h/h2h-stat-row';
import UI_H2HSwap from '../../components/mix/h2h/h2h-swap';
import UI_H2HEmptySlot from '../../components/mix/h2h/h2h-empty-slot';
import RcNationCard from './rc-nation-card';
import type { RecordCompareScore } from '../../../hooks/useRecordsCompare';

/**
 * Шапка сравнения двух стран — та же фигура, что шапка H2H пловцов: сторона · счёт · сторона,
 * под ними строки статов на той же сетке (`h2h-row`).
 *
 * ⚠ **Счёт считается только по общим дисциплинам** (правило 11.3.3). Поэтому строка
 * «only their record» стоит В ШАПКЕ, а не сноской внизу: без неё «76–10» читается как
 * разгром, хотя часть дисциплин просто не с чем сравнивать. Число под счётом говорит
 * знаменатель вслух — ровно как подпись «faster times» в H2H говорит, что счёт не про
 * очные встречи.
 */
interface Props {
  /** Код страны либо null — сторона ещё не выбрана (это законное состояние экрана). */
  a: string | null;
  b: string | null;
  /** Счёт есть только когда выбраны обе: сравнивать одну страну не с чем. */
  score: RecordCompareScore | null;
  /** Сколько рекордов у каждой стороны в этом разрезе (включая несравненные). */
  totals: { a: number; b: number };
  /** Какую сторону заполнит следующий выбор в пикере. */
  active: 'a' | 'b';
  onSwap: () => void;
  /** Выбрать другую страну на эту сторону — и у занятой карточки, и у пустого слота. */
  onFocus: (side: 'a' | 'b') => void;
}

const winnerOf = (left: number, right: number) =>
  left === right ? null : (left > right ? 'left' as const : 'right' as const);

const RcH2HHeader: React.FC<Props> = ({
  a, b, score, totals, active, onSwap, onFocus,
}) => {
  /** Сторона шапки: выбранная страна — карточкой, пустая — слотом «choose a country». */
  const side = (which: 'a' | 'b', code: string | null, records: number) => (
    code
      ? (
        <RcNationCard
          code={code}
          records={records}
          align={which === 'a' ? 'left' : 'right'}
          active={active === which}
          onSelect={() => onFocus(which)}
        />
      )
      : (
        <UI_H2HEmptySlot
          label="בחר מדינה · choose a country"
          active={active === which}
          onClick={() => onFocus(which)}
        />
      )
  );

  return (
    <div className="h2h-compare">
      <div className="h2h-row h2h-row--slots">
        {side('a', a, totals.a)}

        {/* Пока сторон меньше двух, в центре стоит «vs», а не счёт: ноль-ноль читался бы
            как результат сравнения, которого ещё не было. */}
        {score ? (
          <div className="h2h-score">
            <div className="h2h-score__value">{score.a}–{score.b}</div>
            <div className="h2h-score__cap">
              faster records{score.tie > 0 ? ` · ${score.tie} tied` : ''}
            </div>
            <UI_H2HSwap onSwap={onSwap} />
          </div>
        ) : (
          <div className="h2h-vs">vs</div>
        )}

        {side('b', b, totals.b)}
      </div>

      {score && (
        <div className="h2h-stats">
          <UI_H2HStatRow
            label={`events compared · ${score.compared}`}
            left={score.a}
            right={score.b}
            winner={winnerOf(score.a, score.b)}
          />
          {/* Победителя у этой строки НЕТ намеренно: «у соперника нет рекорда» — это про
              покрытие справочника, а не про то, кто быстрее. Подсветить её значило бы
              выдать пустоту за победу — ровно то, что запрещает 11.3.3. */}
          {score.a_only + score.b_only > 0 && (
            <UI_H2HStatRow
              label="no rival record"
              left={score.a_only}
              right={score.b_only}
            />
          )}
        </div>
      )}
    </div>
  );
};

export default RcH2HHeader;
