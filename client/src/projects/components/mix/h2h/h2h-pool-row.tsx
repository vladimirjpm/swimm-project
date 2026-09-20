import React from 'react';
import './h2h.css';
import UI_PoolIcon from '../pool-icon/pool-icon';
import UI_H2HTimeCell from './h2h-time-cell';
import type { SwimQuality } from '../swim-time/swim-time';
import type { RecordKind } from '../record-badge/record-badge';

/**
 * Одна полоса карточки заплыва — ОДИН бассейн (макет 1b, §3): время слева · метка бассейна
 * с разрывом · время справа.
 *
 * Бассейны разнесены потому, что 25м и 50м несравнимы: время короткой воды быстрее по
 * устройству бассейна, и общий разрыв между ними врал бы. Нет пары — нет и разрыва.
 */
export interface H2HPoolSide {
  time?: string | null;
  date?: string | null;
  quality?: SwimQuality | null;
  badge?: 'SB' | { record: RecordKind; scope?: string | null } | null;
  /** Протокол этого заплыва — у КАЖДОЙ стороны свой: времена принадлежат разным людям. */
  href?: string;
  /** Кто держит время — имя и страна под цифрой (вариант `record`). */
  who?: { name: string; countryCode?: string | null } | null;
  /** Подпись к времени («Relay lead-off»). */
  extras?: React.ReactNode;
  /** Плашка победителя принудительно: у рекорда она всегда у пловца, разрыв тут ни при чём. */
  isWinner?: boolean;
  /** Подсказка на времени, когда ссылки нет (WR в варианте `record`). */
  title?: string;
  /** Вид плашки вокруг времени — см. `UI_H2HTimeCell`. */
  box?: 'fill' | 'outline' | 'none';
}

interface Props {
  poolType: string;
  left: H2HPoolSide | null;
  right: H2HPoolSide | null;
  /** «Левое минус правое», мс: отрицательное — быстрее левый. null — плавал только один. */
  deltaMs?: number | null;
  /**
   * Тон разрыва: `win` — цифра в пользу левого (H2H), `behind` — левый медленнее и это
   * норма (рекорд пловца против мирового). В варианте `record` всегда `behind`.
   */
  deltaTone?: 'win' | 'behind';
  /**
   * Что стоит в середине вместо метки бассейна. Заведено под `/records?tab=masters`: там
   * бассейн выбран фильтром на всю страницу и одинаков во всех строках, а различает строки
   * возрастная полоса («25-29»). Не задано — печатается метка бассейна, как было.
   */
  midLabel?: React.ReactNode;
}

/**
 * Разрыв со знаком: «−1.24» (быстрее левый, cyan) / «+0.88» (быстрее правый).
 * Знак обязателен — цифра без него не говорит, в чью пользу она.
 */
const deltaLabel = (ms: number): string =>
  ms === 0 ? '=' : `${ms < 0 ? '−' : '+'}${(Math.abs(ms) / 1000).toFixed(2)}`;

const UI_H2HPoolRow: React.FC<Props> = ({
  poolType, left, right, deltaMs = null, deltaTone = 'win', midLabel,
}) => {
  const leftWins = deltaMs != null && deltaMs < 0;
  const rightWins = deltaMs != null && deltaMs > 0;
  // `behind` — у левой стороны своя плашка (`left.isWinner`), и цифра разрыва не красится
  // «в чью-то пользу»: сравнение не соревнование.
  const deltaClass = deltaTone === 'behind'
    ? ' h2h-pool__delta--behind'
    : (leftWins ? ' h2h-pool__delta--win' : '');

  return (
    <div className="h2h-pool">
      <UI_H2HTimeCell
        time={left?.time}
        date={left?.date}
        quality={left?.quality}
        badge={left?.badge ?? null}
        href={left?.href}
        who={left?.who}
        extras={left?.extras}
        title={left?.title}
        box={left?.box}
        isWinner={left?.isWinner ?? leftWins}
        side="left"
      />

      <div className="h2h-pool__mid">
        {/* Метка бассейна — тот же компонент, что в строке заплыва всего продукта:
            «--25m--» / «-----50m-----». */}
        {midLabel ?? (
          <UI_PoolIcon styleType="icon-text-center" label={poolType} labelClassName="h2h-pool__label" />
        )}
        {deltaMs != null && (
          <span className={`h2h-pool__delta${deltaClass}`}>
            {deltaLabel(deltaMs)}
          </span>
        )}
      </div>

      <UI_H2HTimeCell
        time={right?.time}
        date={right?.date}
        quality={right?.quality}
        badge={right?.badge ?? null}
        href={right?.href}
        who={right?.who}
        extras={right?.extras}
        title={right?.title}
        box={right?.box}
        isWinner={right?.isWinner ?? rightWins}
        side="right"
      />
    </div>
  );
};

export default UI_H2HPoolRow;
