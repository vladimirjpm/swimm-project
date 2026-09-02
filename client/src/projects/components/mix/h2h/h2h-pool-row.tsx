import React from 'react';
import './h2h.css';
import UI_PoolIcon from '../pool-icon/pool-icon';
import UI_H2HTimeCell from './h2h-time-cell';
import type { SwimQuality } from '../swim-time/swim-time';

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
  badge?: 'SB' | 'REC' | null;
}

interface Props {
  poolType: string;
  left: H2HPoolSide | null;
  right: H2HPoolSide | null;
  /** «Левое минус правое», мс: отрицательное — быстрее левый. null — плавал только один. */
  deltaMs?: number | null;
  /** Ссылка на заплыв в таблице результатов; без неё полоса не кликабельна. */
  href?: string;
}

/**
 * Разрыв со знаком: «−1.24» (быстрее левый, cyan) / «+0.88» (быстрее правый).
 * Знак обязателен — цифра без него не говорит, в чью пользу она.
 */
const deltaLabel = (ms: number): string =>
  ms === 0 ? '=' : `${ms < 0 ? '−' : '+'}${(Math.abs(ms) / 1000).toFixed(2)}`;

const UI_H2HPoolRow: React.FC<Props> = ({ poolType, left, right, deltaMs = null, href }) => {
  const leftWins = deltaMs != null && deltaMs < 0;
  const rightWins = deltaMs != null && deltaMs > 0;

  const body = (
    <>
      <UI_H2HTimeCell
        time={left?.time}
        date={left?.date}
        quality={left?.quality}
        badge={left?.badge ?? null}
        isWinner={leftWins}
        side="left"
      />

      <div className="h2h-pool__mid">
        {/* Метка бассейна — тот же компонент, что в строке заплыва всего продукта:
            «--25m--» / «-----50m-----». */}
        <UI_PoolIcon styleType="icon-text-center" label={poolType} labelClassName="h2h-pool__label" />
        {deltaMs != null && (
          <span className={`h2h-pool__delta${leftWins ? ' h2h-pool__delta--win' : ''}`}>
            {deltaLabel(deltaMs)}
          </span>
        )}
      </div>

      <UI_H2HTimeCell
        time={right?.time}
        date={right?.date}
        quality={right?.quality}
        badge={right?.badge ?? null}
        isWinner={rightWins}
        side="right"
      />
    </>
  );

  return href
    ? <a className="h2h-pool" href={href}>{body}</a>
    : <div className="h2h-pool">{body}</div>;
};

export default UI_H2HPoolRow;
