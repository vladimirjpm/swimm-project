import React from 'react';
import './record-badge.css';

/**
 * Бейдж рекорда: «REC» (национальный), «REC·AGE» (возрастная ступень), «REC·M» (мастерская
 * полоса). Хендофф `design_handoff_h2h_addrecords` §5, вариант 2b.
 *
 * Один компонент на продукт — так требует хендофф: те же три класса нужны в H2H, на стене
 * рекордов страницы пловца, в таблице результатов и на `/season-best`. Три копии «REC»-спана
 * разъедутся на первом же изменении палитры, а правило тут строгое: ЗОЛОТО — только
 * национальный рекорд, малые рекорды никогда не золотые.
 *
 * `scope` («age 14», «masters 45-49») в бейдж НЕ выводится — только в подсказку: в строке
 * заплыва места нет, а вопрос «какая именно ступень» возникает у единиц.
 */
export type RecordKind = 'national' | 'age' | 'masters';

interface Props {
  kind: RecordKind;
  /** Ступень справочника — уходит в `title`, на экране не показывается. */
  scope?: string | null;
  /**
   * Рекорд установлен ЭТИМ заплывом («★ REC»). Отличается от обычного бейджа тем, что
   * отвечает на другой вопрос: не «чей рекорд на этой дистанции», а «здесь и сейчас он
   * побит». Раньше это была отдельная золотая плашка «★ NEW RECORD» в таблице результатов;
   * бейдж стал общим, но событие осталось событием — отсюда звезда и заливка.
   */
  isNew?: boolean;
  className?: string;
}

const SUFFIX: Record<RecordKind, string> = {
  national: '',
  age: '·AGE',
  masters: '·M',
};

const TITLE: Record<RecordKind, string> = {
  national: 'National record',
  age: 'Age group record',
  masters: 'Masters record',
};

const UI_RecordBadge: React.FC<Props> = ({ kind, scope, isNew = false, className = '' }) => {
  const title = `${isNew ? 'New ' : ''}${isNew ? TITLE[kind].toLowerCase() : TITLE[kind]}`;
  return (
    <span
      className={`rec-badge rec-badge--${kind}${isNew ? ' rec-badge--new' : ''} ${className}`.trim()}
      title={scope ? `${title} — ${scope}` : title}
    >
      {isNew && <span className="rec-badge__star" aria-hidden="true">★</span>}
      REC
      {SUFFIX[kind] && <span className="rec-badge__suffix">{SUFFIX[kind]}</span>}
    </span>
  );
};

export default UI_RecordBadge;
