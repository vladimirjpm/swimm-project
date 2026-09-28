import React from 'react';
import { RsvpBar, RsvpButtons, RsvpStaffRow } from './group-rsvp';
import type { TrainingRsvpState } from '../use-training-rsvp';
import type { GroupTrainingSchedule, NextTraining } from '../types';

/**
 * Блок тренировок в шапке группы (хендофф group-club-changes §4, вариант 2a).
 *
 * Расписание РЕГУЛЯРНОЕ (решение Влада): дни недели + часы, а не список занятий — ближайшее
 * занятие тогда считается само. Считает его СЕРВЕР в поясе Израиля: «сегодня» не должно
 * зависеть от часов зрителя (`next_training` в DTO), клиент только рисует готовое.
 *
 * Два вида по зрителю:
 * - `guest` (не участник) — только расписание и место, без NEXT: ближайшее занятие
 *   постороннему ни к чему, а строка NEXT звала бы его прийти;
 * - `member` (участник или управляющий) — строка NEXT: дата, «завтра», дни и место; под ней
 *   ответы «иду / не иду» (Ш2): полоса со счётчиками; участнику — три кнопки, управляющему —
 *   аватары идущих и «Who's coming →» (в таб Trainings). Управляющий, который сам в
 *   составе, получает и то и другое. Пока ответы не приехали — только строка NEXT.
 *
 * Прежде вместо блока стояли четыре отдельных чипа (дни, место, бассейн, Next) и на
 * телефоне занимали два ряда. Расписания нет → блок не рендерится вовсе: пустая рамка
 * «расписание не заведено» на витрине хуже её отсутствия.
 */

/** Дни недели ISO: 1 = понедельник … 7 = воскресенье (как в JSON расписания). */
const DAY_SHORT: Record<number, string> = {
  1: 'Mon', 2: 'Tue', 3: 'Wed', 4: 'Thu', 5: 'Fri', 6: 'Sat', 7: 'Sun',
};

const MONTH_SHORT = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];

/** yyyy-MM-dd → Date в местном календаре (без сдвига поясом, как при `new Date(iso)`). */
function parseIsoDate(iso: string): Date | null {
  const [y, m, d] = iso.split('-').map(Number);
  if (!y || !m || !d) return null;
  return new Date(y, m - 1, d);
}

/**
 * «Tue 29 Sep» — дата ближайшего занятия. Собрана руками, а не `toLocaleDateString('en-GB')`:
 * свежий ICU пишет сентябрь как «Sept», и дата в строке выходила бы то одной длины, то другой.
 */
function formatNextDate(iso: string): string {
  const date = parseIsoDate(iso);
  if (!date) return iso;
  const dow = date.getDay() === 0 ? 7 : date.getDay();
  return `${DAY_SHORT[dow]} ${date.getDate()} ${MONTH_SHORT[date.getMonth()]}`;
}

/** Сегодня / завтра — по той же локальной дате, что видит зритель; иначе ничего. */
function relativeLabel(iso: string): string | null {
  const target = parseIsoDate(iso);
  if (!target) return null;
  const today = new Date();
  const days = Math.round(
    (target.getTime() - new Date(today.getFullYear(), today.getMonth(), today.getDate()).getTime())
    / 86400000,
  );
  if (days === 0) return 'today';
  if (days === 1) return 'tomorrow';
  return null;
}

/** «Tue · Sat · Sun» — дни расписания; им же подписан чип липкой полосы. */
function scheduleDaysLabel(schedule?: GroupTrainingSchedule | null): string | null {
  if (!schedule || schedule.slots.length === 0) return null;
  return schedule.slots.map((s) => DAY_SHORT[s.day] ?? '?').join(' · ');
}

const slotTime = (s: { start: string; end?: string | null }) => `${s.start}${s.end ? `–${s.end}` : ''}`;

const LABEL = 'text-[9.5px] font-extrabold uppercase tracking-[.08em] min-[960px]:text-[10px]';

function GroupTrainingBlock({
  schedule, next, mode, rsvp, onWhosComing,
}: {
  schedule?: GroupTrainingSchedule | null;
  next?: NextTraining | null;
  mode: 'guest' | 'member';
  /** Ответы на ближайшее занятие (один экземпляр на страницу); нет — блок только сообщает. */
  rsvp?: TrainingRsvpState | null;
  /** «Who's coming →» — в таб Trainings, к списку людей. */
  onWhosComing?: () => void;
}) {
  if (!schedule || schedule.slots.length === 0) return null;

  const pool = schedule.pool_type ?? next?.pool_type ?? null;
  const place = schedule.place ?? next?.place ?? null;

  if (mode === 'member' && next) {
    const relative = relativeLabel(next.date);
    const r = rsvp?.rsvp && rsvp.rsvp.session_id === next.id ? rsvp.rsvp : null;
    const showButtons = !!r && r.is_member && r.can_answer;
    const showStaff = !!r && r.can_manage;
    // Раскладка (хендофф 2a / 4a): телефон — столбиком NEXT → полоса → кнопки → строка
    // тренера; десктоп — сетка «NEXT | кнопки», полоса на всю ширину под ними. Порядок
    // задают `order-*`, а не две разметки: элемент один, меняется только место.
    return (
      <div
        className="grid grid-cols-1 gap-2.5 rounded-[12px] border px-3 py-2.5 min-[960px]:grid-cols-[minmax(0,1fr)_auto] min-[960px]:items-center min-[960px]:gap-x-6 min-[960px]:gap-y-3.5 min-[960px]:px-4 min-[960px]:py-3.5"
        style={{ borderColor: 'var(--deep-live-border)', background: 'var(--deep-card-bg)' }}
      >
        <div className="order-1 flex min-w-0 items-center gap-2.5">
          <span
            aria-hidden="true"
            className="h-2 w-2 flex-none rounded-full min-[960px]:h-[9px] min-[960px]:w-[9px]"
            style={{
              background: 'var(--deep-live)',
              boxShadow: '0 0 0 3px color-mix(in srgb, var(--deep-live) 14%, transparent)',
            }}
          />
          <div className="min-w-0 flex-1">
            <div className="flex flex-wrap items-baseline gap-x-1.5 gap-y-0.5 min-[960px]:gap-x-2">
              <span className={LABEL} style={{ color: 'var(--deep-live)' }}>Next</span>
              <span className="hp-mono text-[13px] font-extrabold min-[960px]:text-[15px]" style={{ color: 'var(--deep-text)' }}>
                {formatNextDate(next.date)} · {next.start}
              </span>
              {relative && (
                <span className="text-[11.5px] font-bold min-[960px]:text-[12.5px]" style={{ color: 'var(--deep-text-mute)' }}>
                  {relative}
                </span>
              )}
            </div>
            {/* Место — в <bdi>: название бассейна ивритское, и без изоляции RTL-перестановка
                утащила бы разделители «·» не на ту сторону (поймано 09.09.2026). */}
            <div className="hp-mono mt-[3px] text-[11px] font-bold min-[960px]:text-[12px]" style={{ color: 'var(--deep-text-mute)' }}>
              {scheduleDaysLabel(schedule)}
              {place && <> · <bdi>{place}</bdi></>}
              {pool && <> · {pool}</>}
            </div>
          </div>
        </div>

        {r && r.total > 0 && (
          <div className="order-2 min-[960px]:order-3 min-[960px]:col-span-2">
            <RsvpBar rsvp={r} />
          </div>
        )}

        {showButtons && (
          <div className="order-3 min-[960px]:order-2">
            <RsvpButtons current={r.mine?.answer ?? null} onAnswer={(a) => { void rsvp!.answer(a); }} />
          </div>
        )}

        {showStaff && (
          <div
            className={`order-4 border-t pt-2 ${showButtons
              ? 'min-[960px]:col-span-2'
              : 'min-[960px]:order-2 min-[960px]:border-t-0 min-[960px]:pt-0'}`}
            style={{ borderColor: 'var(--deep-divider)' }}
          >
            <RsvpStaffRow rsvp={r} onOpen={() => onWhosComing?.()} />
          </div>
        )}

        {rsvp?.error && (
          <div className="order-5 text-[12px] font-extrabold min-[960px]:col-span-2" style={{ color: 'var(--deep-danger)' }}>
            {rsvp.error}
          </div>
        )}

        {schedule.note && (
          <div className="order-6 min-[960px]:col-span-2"><ScheduleNote note={schedule.note} /></div>
        )}
      </div>
    );
  }

  return (
    <div
      className="flex flex-col gap-1.5 rounded-[12px] border px-3 py-2.5 min-[960px]:flex-row min-[960px]:flex-wrap min-[960px]:items-center min-[960px]:gap-x-[18px] min-[960px]:px-4 min-[960px]:py-3"
      style={{ borderColor: 'var(--deep-card-border)', background: 'var(--deep-card-bg)' }}
    >
      <span className={LABEL} style={{ color: 'var(--deep-text-mute)' }}>Trainings</span>
      <div className="hp-mono flex flex-wrap gap-x-3.5 gap-y-1 text-[12px] font-bold min-[960px]:gap-x-4 min-[960px]:text-[13px]" style={{ color: 'var(--deep-text-mute)' }}>
        {schedule.slots.map((s) => (
          <span key={`${s.day}-${s.start}`} className="whitespace-nowrap">
            <b style={{ color: 'var(--deep-text)' }}>{DAY_SHORT[s.day] ?? '?'}</b> {slotTime(s)}
          </span>
        ))}
      </div>
      {(place || pool) && (
        <span className="text-[12px] font-bold min-[960px]:text-[13px]" style={{ color: 'var(--deep-text-mute)' }}>
          {place && <>📍 <bdi className="font-extrabold" style={{ color: 'var(--deep-text)' }}>{place}</bdi></>}
          {place && pool && ' · '}
          {pool && `${pool} pool`}
        </span>
      )}
      {schedule.note && <ScheduleNote note={schedule.note} />}
    </div>
  );
}

function ScheduleNote({ note }: { note: string }) {
  return (
    <span dir="auto" className="text-[11.5px] italic" style={{ color: 'var(--deep-text-mute)' }}>
      {note}
    </span>
  );
}

export default GroupTrainingBlock;
export { DAY_SHORT, formatNextDate, scheduleDaysLabel };
