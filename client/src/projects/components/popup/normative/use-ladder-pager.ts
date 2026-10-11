import { useState } from 'react';
import type { Cat } from './normative-model';

type Level = { key: string; label: string; cat: Cat };

/**
 * Мобильный пейджер по лестнице уровней: пара [текущий, следующий (быстрее)] и шаги ‹ ›.
 * Пока пользователь не листал — пара стоит на уровне пловца (`myIdx`).
 */
export const useLadderPager = (ladder: Level[], myIdx: number) => {
  const [pairIdx, setPairIdx] = useState<number | null>(null); // мобильный пейджер
  const [navDir, setNavDir] = useState<'next' | 'prev' | null>(null);

  // ==== Мобильный пейджер: пара [текущий, следующий (быстрее)] ====
  const rawPairIdx = pairIdx ?? (myIdx > 0 ? myIdx : 1);
  const pIdx = Math.max(1, Math.min(ladder.length - 1, rawPairIdx));
  const leftLevel = ladder[pIdx];        // текущий (фокус)
  const rightLevel = ladder[pIdx - 1];   // следующий, быстрее
  const canNext = pIdx > 1;                    // к более сильным
  const canPrev = pIdx < ladder.length - 1;    // к более слабым
  const goPrev = () => { if (!canPrev) return; setPairIdx(pIdx + 1); setNavDir('prev'); };
  const goNext = () => { if (!canNext) return; setPairIdx(pIdx - 1); setNavDir('next'); };

  return { navDir, pIdx, leftLevel, rightLevel, canNext, canPrev, goPrev, goNext };
};
