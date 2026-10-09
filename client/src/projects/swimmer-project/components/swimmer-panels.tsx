import React from 'react';
import { MIN_PEERS_FOR_RANK } from '../../components/mix/rank-of-peers/rank-of-peers';
import type { SwimmerDisciplineRank } from '../use-swimmer-page';

/**
 * Панели табов страницы спортсмена (BLOCKS.md §5–9). Каждая — независимый блок: свои данные,
 * никаких собственных фильтров, кроме тумблера 25m/50m в PB (единственное исключение §7).
 *
 * Пустое состояние — норма, а не край: панель рендерится с текстом «нет заплывов в этом
 * сезоне», карусель и табы остаются на месте.
 */

/**
 * Сколько сверстников должно быть в дисциплине, чтобы место вообще что-то значило.
 * Меньше двух — «первый среди одного»: у мастерса 1958 года рождения на 200 на спине
 * в сезоне вообще нет соперников, и бейдж SB там читался бы как достижение, которым не
 * является. Само место сервер отдаёт как есть — прятать данные нельзя, но и хвастаться ими
 * тоже; поэтому порог живёт здесь, в одном месте на оба вида.
 */
/** Порог один на продукт и живёт рядом с подписью «alone», которую он же включает. */
const MIN_PEERS_FOR_SB = MIN_PEERS_FOR_RANK;

/** Первое место в группе, которое действительно является местом (см. MIN_PEERS_FOR_SB). */
export const holdsSeasonBest = (r: SwimmerDisciplineRank) =>
  r.rank === 1 && r.peerCount >= MIN_PEERS_FOR_SB;

/**
 * Состояние загрузки блока — ровно то, что отдаёт `useJson`, поэтому в панель передаётся
 * сам результат хука (`state={bestTimes}`), а не разобранный на булевы.
 */
export interface PanelLoad {
  loading: boolean;
  error: boolean;
}

/**
 * Текст панели, у которой данных НЕТ. Состояний три, и слить их в одно «Loading…» — значит
 * соврать: при сетевой ошибке страница крутила бы вечный спиннер (баг Season best,
 * 2026-08-25), а при несостоявшемся запросе — обещала данные, которых никто не просил.
 * Поэтому «грузимся» и «не грузимся, но пусто» — разные надписи, и обе живут здесь.
 */
export const noDataText = (loading: boolean, whenIdle: string) => (loading ? 'Loading…' : whenIdle);

export function PanelEmpty({ children }: { children: React.ReactNode }) {
  return <div className="deep-empty">{children}</div>;
}

export function PanelHead({ title, hint, right }: { title: string; hint?: string; right?: React.ReactNode }) {
  return (
    <div className="deep-panel-head">
      <div>
        <div className="deep-panel-title">{title}</div>
        {hint && <div className="deep-panel-hint">{hint}</div>}
      </div>
      {right}
    </div>
  );
}
