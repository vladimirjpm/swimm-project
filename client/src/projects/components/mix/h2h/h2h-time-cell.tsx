import React from 'react';
import './h2h.css';
import UI_SwimTime, { type SwimQuality } from '../swim-time/swim-time';
import UI_RecordBadge, { type RecordKind } from '../record-badge/record-badge';

/**
 * Ячейка времени одной стороны (макет 1b, §3).
 *
 * Победитель заключён в cyan-плашку — без свечения: glow в этом экране носит только
 * включённое сердечко. Время рисуется через `UI_SwimTime` (правило продукта: время везде
 * одним компонентом, вместе с признаком качества).
 *
 * Строка «время + бейдж» — flex с `nowrap`: без него бейдж переносится под время
 * (проверено в макете и воспроизводится в проде).
 */
interface Props {
  time?: string | null;
  date?: string | null;
  quality?: SwimQuality | null;
  isWinner?: boolean;
  /**
   * Бейдж: `'SB'` — быстрейший среди сверстников, либо рекорд с его классом. Вместе они не
   * показываются — рекорд важнее (хендофф §5), выбор делает вызывающий.
   */
  badge?: 'SB' | { record: RecordKind; scope?: string | null } | null;
  side: 'left' | 'right';
}

const UI_H2HTimeCell: React.FC<Props> = ({
  time, date, quality, isWinner = false, badge = null, side,
}) => {
  if (!time) {
    return (
      <div className={`h2h-time h2h-time--${side}`}>
        <div className="h2h-time__empty">—</div>
      </div>
    );
  }

  const body = (
    <>
      <div className="h2h-time__line">
        <UI_SwimTime time={time} quality={quality} className="h2h-time__value" />
        {badge === 'SB' && <span className="h2h-badge h2h-badge--sb">SB</span>}
        {badge && badge !== 'SB' && (
          <UI_RecordBadge kind={badge.record} scope={badge.scope} />
        )}
      </div>
      {date && <div className="h2h-time__date">{date}</div>}
    </>
  );

  return (
    <div className={`h2h-time h2h-time--${side}`}>
      {isWinner ? <div className="h2h-time__box">{body}</div> : body}
    </div>
  );
};

export default UI_H2HTimeCell;
