import React from 'react';
import type { GroupTrainingSchedule, NextTraining } from '../types';

/**
 * Слоты шапки группы: «Training info» (регулярное расписание) и «Next training» (ближайшее
 * занятие). Место под них было предусмотрено планом каркаса (§5.2), заполнены 09.09.2026.
 *
 * Расписание РЕГУЛЯРНОЕ (решение Влада): дни недели + часы, а не список занятий — «Next
 * training» тогда считается сам. Считает его СЕРВЕР в поясе Израиля: «сегодня» не должно
 * зависеть от часов зрителя (`next_training` в DTO), клиент только рисует готовое.
 *
 * Расписания нет → компонент не рендерится вовсе: пустая рамка «расписание не заведено» на
 * витрине группы хуже её отсутствия.
 */

/** Дни недели ISO: 1 = понедельник … 7 = воскресенье (как в JSON расписания). */
const DAY_SHORT: Record<number, string> = {
  1: 'Mon', 2: 'Tue', 3: 'Wed', 4: 'Thu', 5: 'Fri', 6: 'Sat', 7: 'Sun',
};

/** «Wed, 11 Sep» — дата ближайшего занятия. Локаль en: UI сайта только английский. */
function formatNextDate(iso: string): string {
  const [y, m, d] = iso.split('-').map(Number);
  if (!y || !m || !d) return iso;
  const date = new Date(y, m - 1, d);
  return date.toLocaleDateString('en-GB', { weekday: 'short', day: 'numeric', month: 'short' });
}

/** Сегодня / завтра — по той же локальной дате, что видит зритель; иначе просто дата. */
function relativeLabel(iso: string): string | null {
  const [y, m, d] = iso.split('-').map(Number);
  if (!y || !m || !d) return null;
  const target = new Date(y, m - 1, d);
  const today = new Date();
  const days = Math.round(
    (new Date(target.getFullYear(), target.getMonth(), target.getDate()).getTime()
      - new Date(today.getFullYear(), today.getMonth(), today.getDate()).getTime())
    / 86400000,
  );
  if (days === 0) return 'today';
  if (days === 1) return 'tomorrow';
  return null;
}

function SlotChip({ children, accent }: { children: React.ReactNode; accent?: boolean }) {
  return (
    <span
      className="hp-mono inline-flex items-center gap-1.5 whitespace-nowrap rounded-[9px] border px-2.5 py-[6px] text-[11.5px] font-extrabold"
      style={{
        borderColor: accent ? 'var(--deep-accent-border)' : 'var(--deep-card-border)',
        background: accent ? 'var(--deep-accent-chip)' : 'transparent',
        color: accent ? 'var(--deep-accent)' : 'var(--deep-text-mute)',
      }}
    >
      {children}
    </span>
  );
}

function GroupTrainingSlots({
  schedule, next,
}: {
  schedule?: GroupTrainingSchedule | null;
  next?: NextTraining | null;
}) {
  if (!schedule || schedule.slots.length === 0) return null;

  // Одинаковые часы у всех дней — самый частый случай, и тогда строка читается как
  // «Mon · Wed · Fri 18:00–19:30», а не тремя повторами одного и того же времени.
  const times = new Set(schedule.slots.map((s) => `${s.start}${s.end ? `–${s.end}` : ''}`));
  const sameTime = times.size === 1 ? [...times][0] : null;

  const relative = next ? relativeLabel(next.date) : null;

  return (
    <div className="mt-4 flex flex-wrap items-center gap-2">
      <SlotChip>
        🗓
        {sameTime ? (
          <>
            <span>{schedule.slots.map((s) => DAY_SHORT[s.day] ?? '?').join(' · ')}</span>
            <span style={{ color: 'var(--deep-text)' }}>{sameTime}</span>
          </>
        ) : (
          <span>
            {schedule.slots
              .map((s) => `${DAY_SHORT[s.day] ?? '?'} ${s.start}${s.end ? `–${s.end}` : ''}`)
              .join(' · ')}
          </span>
        )}
      </SlotChip>

      {/* Место — СВОИМ чипом и с dir="auto": название бассейна ивритское, и приписка
          «25m» внутри той же строки уезжала бы влево от него (RTL-перестановка, поймано
          09.09.2026). Тип бассейна поэтому отдельным чипом, он всегда LTR. */}
      {schedule.place && (
        <SlotChip>
          📍 <span dir="auto">{schedule.place}</span>
        </SlotChip>
      )}

      {schedule.pool_type && <SlotChip>{schedule.pool_type}</SlotChip>}

      {next && (
        <SlotChip accent>
          Next: {formatNextDate(next.date)}, {next.start}
          {relative ? ` (${relative})` : ''}
        </SlotChip>
      )}

      {schedule.note && (
        <span className="text-[11.5px] italic" style={{ color: 'var(--deep-text-mute)' }}>
          {schedule.note}
        </span>
      )}
    </div>
  );
}

export default GroupTrainingSlots;
export { DAY_SHORT };
