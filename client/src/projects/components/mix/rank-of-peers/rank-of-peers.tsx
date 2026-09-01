import React from 'react';

/**
 * Место в рейтинге вместе с размером круга сравнения: «#1» и под ним «of 36».
 *
 * Две цифры всегда ходят парой и по отдельности врут: «#1» без круга не отличает первого
 * из тридцати шести от первого из двух, а «of 36» само по себе ничего не значит. Держим их
 * в одном компоненте, чтобы порядок (место сверху, круг снизу), правило «alone» и подпись
 * для скринридера не разъехались по экранам.
 *
 * ⚠ Это НЕ `UI_PositionBadge`: тот показывает место В ПРОТОКОЛЕ и красит 1-2-3 медалью.
 * В сезонном рейтинге медалей не дают, и медальный кружок соврал бы — там первое место
 * это season best, а не золото.
 *
 * Тем-нейтрален: своих цветов и кеглей не держит, всё приезжает через `className` /
 * `captionClassName` от вызывающего (на deep-странице это токены `--sr-*` строки заплыва).
 */

/**
 * Меньше двух ровесников — сравнивать не с кем, и «место» становится фикцией: единственный
 * в группе автоматически первый. Такой случай подписываем «alone», а не «#1 of 1».
 */
export const MIN_PEERS_FOR_RANK = 2;

interface Props {
  rank: number;
  /**
   * Сколько ровесников в круге сравнения (включая самого пловца). null/undefined — счёт
   * неизвестен, подпись не рисуется и остаётся одно место.
   */
  peerCount?: number | null;
  /** Первое место — это и есть season best; красит вызывающий, компонент лишь помечает. */
  isFirst?: boolean;
  /** Кегль и цвет самого места. */
  className?: string;
  /** Кегль и цвет подписи «of 36». */
  captionClassName?: string;
}

/** «of 36» либо «alone» — правило одно на продукт (см. MIN_PEERS_FOR_RANK). */
export function peersLabel(peerCount?: number | null): string | null {
  if (peerCount == null) return null;
  return peerCount < MIN_PEERS_FOR_RANK ? 'alone' : `of ${peerCount}`;
}

const UI_RankOfPeers: React.FC<Props> = ({
  rank,
  peerCount = null,
  isFirst = false,
  className = '',
  captionClassName = '',
}) => {
  const caption = peersLabel(peerCount);

  return (
    <span
      className="dv-rank-of-peers flex flex-col items-center leading-tight"
      // Скринридеру пара «#1 / of 36» без пояснения читается как два обрывка.
      title={
        caption === 'alone'
          ? `Rank ${rank} — nobody else in this group`
          : caption
            ? `Rank ${rank} of ${peerCount}`
            : `Rank ${rank}`
      }
    >
      <span className={`dv-rank-of-peers__rank${isFirst ? ' is-first' : ''} ${className}`}>
        #{rank}
      </span>
      {caption && (
        <span className={`dv-rank-of-peers__peers ${captionClassName}`}>{caption}</span>
      )}
    </span>
  );
};

export default UI_RankOfPeers;
