import React from 'react';
import type { CalendarMeet } from './calendar-types';
import MeetCube from './meet-cube';

/*
 * Сетка кубов соревнований. Колонки — от места: `sidebar` — правая колонка лейаута 9a рядом с
 * мини-месяцем (2 на 1024–1279, 3 с 1280), `full` — без сайдбара (телефон: 1, с 640 — 2).
 * Многодневка в процессе сама занимает 2 колонки (`MeetCube`, `isWideCube`).
 */

const COLUMNS = {
  sidebar: 'grid-cols-1 sm:grid-cols-2 xl:grid-cols-3',
  full: 'grid-cols-1 sm:grid-cols-2',
} as const;

function CubeGrid({
  meets,
  today,
  columns = 'full',
}: {
  meets: CalendarMeet[];
  today: string;
  columns?: keyof typeof COLUMNS;
}) {
  return (
    <div className={`grid gap-[10px] ${COLUMNS[columns]}`}>
      {meets.map((meet) => (
        <MeetCube key={meet.key} meet={meet} today={today} />
      ))}
    </div>
  );
}

export default CubeGrid;
