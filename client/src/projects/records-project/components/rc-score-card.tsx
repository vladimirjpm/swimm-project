import React from 'react';
import UI_FlagEmoji from '../../components/mix/flag-icon/flag-icon';
import type { RecordCompareScore } from '../../../hooks/useRecordsCompare';

/**
 * Сводный счёт сравнения двух стран.
 *
 * ⚠ **Знаменатель — общие дисциплины, а не все.** Дисциплины, где рекорд есть только у
 * одной стороны, вынесены ОТДЕЛЬНОЙ строкой «не сравнивались» и в счёт не идут: иначе
 * страна с половинным покрытием выигрывала бы пустотами (требование 11.3.3). Эта строка —
 * не сноска мелким шрифтом, а часть ответа: без неё «76:10» выглядит как полный разгром,
 * хотя четыре дисциплины просто не с чем сравнивать.
 */

interface Props {
  a: string;
  b: string;
  score: RecordCompareScore;
}

const RcScoreCard: React.FC<Props> = ({ a, b, score }) => {
  const leads = score.a > score.b ? 'a' : score.b > score.a ? 'b' : 'tie';
  const uncompared = score.a_only + score.b_only;

  return (
    <div className="rc-score">
      <div className="rc-score__line">
        <div className={`rc-score__side${leads === 'a' ? ' rc-score__side--lead' : ''}`}>
          <UI_FlagEmoji countryCode={a} size="32x24" className="src-rc-score-card" />
          <span className="rc-score__code">{a}</span>
          <span className="rc-score__num">{score.a}</span>
        </div>

        <span className="rc-score__vs">:</span>

        <div className={`rc-score__side rc-score__side--right${leads === 'b' ? ' rc-score__side--lead' : ''}`}>
          <span className="rc-score__num">{score.b}</span>
          <span className="rc-score__code">{b}</span>
          <UI_FlagEmoji countryCode={b} size="32x24" className="src-rc-score-card" />
        </div>
      </div>

      <div className="rc-score__note">
        {score.compared} {score.compared === 1 ? 'event' : 'events'} compared
        {score.tie > 0 && <> · {score.tie} tied</>}
        {uncompared > 0 && (
          <span className="rc-score__uncompared">
            {' · '}
            {uncompared} not compared
            <span className="rc-score__uncompared-why">
              {' '}({score.a_only > 0 && `${score.a_only} only ${a}`}
              {score.a_only > 0 && score.b_only > 0 && ', '}
              {score.b_only > 0 && `${score.b_only} only ${b}`})
            </span>
          </span>
        )}
      </div>
    </div>
  );
};

export default RcScoreCard;
