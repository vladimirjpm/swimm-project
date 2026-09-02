import React from 'react';
import './h2h.css';
import UI_RecordBadge from '../record-badge/record-badge';
import type { H2HRecordCounts } from './h2h.types';

/**
 * Три счётчика рекордов одной стороны в строке статов: «1 REC · 4 REC·AGE · 0 REC·M»
 * (хендофф `design_handoff_h2h_addrecords` §5).
 *
 * Классы НЕ складываются в одну цифру: у подростка десяток возрастных ступеней одного и
 * того же достижения, у взрослого — один национальный, и общая сумма («115 против 5»)
 * преувеличивала бы разрыв. Национальный поэтому и крупнее остальных.
 *
 * Нули показываем: ряд обязан быть симметричным, иначе цифры сторон перестают стоять друг
 * под другом. Подсветки лидера здесь нет — цветов в строке и так хватает.
 */
interface Props {
  counts: H2HRecordCounts;
  align: 'left' | 'right';
}

const UI_H2HRecordCounts: React.FC<Props> = ({ counts, align }) => (
  <span className={`h2h-recs h2h-recs--${align}`}>
    <span className="h2h-recs__item h2h-recs__item--major">
      <span className="h2h-recs__num">{counts.national}</span>
      <UI_RecordBadge kind="national" />
    </span>
    <span className="h2h-recs__item">
      <span className="h2h-recs__num">{counts.age}</span>
      <UI_RecordBadge kind="age" />
    </span>
    <span className="h2h-recs__item">
      <span className="h2h-recs__num">{counts.masters}</span>
      <UI_RecordBadge kind="masters" />
    </span>
  </span>
);

export default UI_H2HRecordCounts;
