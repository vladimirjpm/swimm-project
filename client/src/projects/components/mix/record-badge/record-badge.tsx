import React from 'react';
import './record-badge.css';

/**
 * Бейдж рекорда: «NR» (национальный), «WR» (мировой), «REC·AGE» (возрастная ступень),
 * «REC·M» (мастерская полоса). Хендофф `design_handoff_h2h_addrecords` §5, вариант 2b,
 * плюс решение Влада 02.09.2026 — у рекордов страны и мира свои общепринятые сокращения,
 * а не общее «REC»: их путали с возрастными.
 *
 * Один компонент на продукт — так требует хендофф: те же классы нужны в H2H, на стене
 * рекордов пловца и клуба, в таблице результатов и на `/season-best`. Копии «REC»-спана
 * разъедутся на первом же изменении палитры, а правило тут строгое: ЗОЛОТО — только
 * рекордам страны и выше (NR, WR); возрастные и мастерские никогда не золотые.
 *
 * `scope` («age 14», «masters 45-49») в бейдж НЕ выводится — только в подсказку: в строке
 * заплыва места нет, а вопрос «какая именно ступень» возникает у единиц.
 */
export type RecordKind = 'world' | 'national' | 'age' | 'masters';

interface Props {
  kind: RecordKind;
  /** Ступень справочника — уходит в `title`, на экране не показывается. */
  scope?: string | null;
  /**
   * Рекорд установлен ЭТИМ заплывом («★ NR»). Отличается от обычного бейджа тем, что
   * отвечает на другой вопрос: не «чей рекорд на этой дистанции», а «здесь и сейчас он
   * побит». Раньше это была отдельная золотая плашка «★ NEW RECORD» в таблице результатов;
   * бейдж стал общим, но событие осталось событием — отсюда звезда и заливка.
   */
  isNew?: boolean;
  /**
   * Пловец ДЕРЖИТ этот рекорд (бейдж стоит у имени в общем списке заплывов). Без пометки
   * «HOLDER» голый «REC·M» рядом с именем читается как «этот заплыв — рекорд», хотя он
   * может быть каким угодно: вопрос-то был про человека, а не про строку (замечено Владом
   * 02.09.2026 на таблице результатов).
   *
   * Там, где контекст сам всё объясняет — стена рекордов пловца и клуба, — пометка не
   * нужна: в списке рекордов иначе и не бывает.
   */
  isHolder?: boolean;
  className?: string;
}

/** Основная надпись: у рекордов страны и мира — свои сокращения, у «малых» общее «REC». */
const LABEL: Record<RecordKind, string> = {
  world: 'WR',
  national: 'NR',
  age: 'REC',
  masters: 'REC',
};

/** Уточнение класса — тише основной надписи: сначала читается «REC». */
const SUFFIX: Record<RecordKind, string> = {
  world: '',
  national: '',
  age: '·AGE',
  masters: '·M',
};

const TITLE: Record<RecordKind, string> = {
  world: 'World record',
  national: 'National record',
  age: 'Age group record',
  masters: 'Masters record',
};

const UI_RecordBadge: React.FC<Props> = ({
  kind, scope, isNew = false, isHolder = false, className = '',
}) => {
  const title = isNew
    ? `New ${TITLE[kind].toLowerCase()}`
    : isHolder ? `Holds the ${TITLE[kind].toLowerCase()}` : TITLE[kind];
  return (
    <span
      className={`rec-badge rec-badge--${kind}${isNew ? ' rec-badge--new' : ''} ${className}`.trim()}
      title={scope ? `${title} — ${scope}` : title}
    >
      {isNew && <span className="rec-badge__star" aria-hidden="true">★</span>}
      {isHolder && <span className="rec-badge__prefix">HOLDER</span>}
      {LABEL[kind]}
      {SUFFIX[kind] && <span className="rec-badge__suffix">{SUFFIX[kind]}</span>}
    </span>
  );
};

export default UI_RecordBadge;
