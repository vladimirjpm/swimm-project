import React from 'react';
import './h2h.css';
import UI_SwimTime, { type SwimQuality } from '../swim-time/swim-time';
import UI_RecordBadge, { type RecordKind } from '../record-badge/record-badge';
import UI_FlagEmoji from '../flag-icon/flag-icon';

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
  /**
   * Протокол ЭТОГО заплыва. Ссылка живёт у ячейки, а не у строки бассейна: строка держит
   * времена ДВУХ разных людей, и одна ссылка на обе половины уводила клик по правому
   * времени в протокол левого пловца (поймано 02.09.2026 на паре 7424/62115).
   */
  href?: string;
  /**
   * Кто держит это время — имя и страна (alpha-3) под временем. Нужно варианту `record`:
   * слева пловец, справа держатель мирового рекорда. В обычном H2H имена стоят в шапке
   * сравнения, и здесь их не дублируют.
   */
  who?: { name: string; countryCode?: string | null } | null;
  /** Подпись к времени — «Relay lead-off» и прочее, что объясняет саму цифру. */
  extras?: React.ReactNode;
  /** Подсказка на самом времени — там, где ссылки нет (мировой рекорд). */
  title?: string;
  /**
   * Как выглядит плашка вокруг времени, когда она есть (`isWinner`):
   *
   * - `fill` — **по умолчанию**: заливка и рамка. Так выглядит победитель в H2H, где плашка
   *   и есть способ сказать «этот быстрее».
   * - `outline` — только рамка, без заливки.
   * - `none` — ни заливки, ни рамки: плашка остаётся лишь коробкой выравнивания. Так стоит
   *   вариант `record` (решение Влада 20.09.2026) — там время и так золотое, а колонку
   *   очерчивают разделители строк.
   *
   * Геометрия во всех трёх одинаковая: `none` гасит рамку ЦВЕТОМ, а не убирает её, иначе
   * стороны разъезжаются на её ширину.
   */
  box?: 'fill' | 'outline' | 'none';
  side: 'left' | 'right';
}

const UI_H2HTimeCell: React.FC<Props> = ({
  time, date, quality, isWinner = false, badge = null, href, who = null, extras, title,
  box = 'fill', side,
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
      {who && (
        <div className="h2h-time__who">
          {who.countryCode && (
            <UI_FlagEmoji countryCode={who.countryCode} size="16x12" className="h2h-time__flag" />
          )}
          <span className="h2h-time__who-name">{who.name}</span>
        </div>
      )}
      {date && <div className="h2h-time__date">{date}</div>}
    </>
  );

  const inner = isWinner
    ? <div className={`h2h-time__box${box === 'fill' ? '' : ` h2h-time__box--${box}`}`}>{body}</div>
    : body;

  return (
    <div className={`h2h-time h2h-time--${side}`} title={title}>
      {href ? <a className="h2h-time__link" href={href}>{inner}</a> : inner}
      {/* Подпись ВНЕ ссылки и вне плашки: она объясняет время, а не является им. */}
      {extras && <div className="h2h-time__extra">{extras}</div>}
    </div>
  );
};

export default UI_H2HTimeCell;
