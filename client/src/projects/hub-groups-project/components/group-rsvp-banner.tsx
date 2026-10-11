import React from 'react';
import { RsvpButtons } from './group-rsvp';
import { formatNextDate, relativeLabel } from './group-training-slots';
import type { TrainingRsvpState } from '../use-training-rsvp';
import type { HubGroupDetails } from '../types';

/**
 * Режим «сверху» (хендофф group-club-changes §4 «Mode top», план
 * docs/plans/entity-hero-roles-plan.md, Ш4): участнику, который ещё не ответил на ближайшее
 * занятие, карточка «Are you coming?» встаёт НАД фото, прямо под топбаром. Блок NEXT в шапке
 * на это время прячется; после ответа баннер уходит, и в шапку возвращается обычный блок с
 * нажатой кнопкой (ответ оптимистичный — уходит сразу, отказ сервера вернёт баннер).
 *
 * Включает тренер галкой в расписании (`rsvp_top`, по умолчанию выключено). Гостю и тренеру
 * баннера нет никогда; участнику на перерыве — тоже (звать его — не то, что он просил).
 */

/** Показывать ли баннер: одно правило для страницы (баннер) и шапки (спрятать NEXT). */
function showRsvpBanner(group: HubGroupDetails, rsvp?: TrainingRsvpState | null): boolean {
  const next = group.next_training;
  const r = rsvp?.rsvp;
  return !!group.training_schedule?.rsvp_top && !!next && !!r && r.session_id === next.id
    && r.is_member && !r.can_manage && r.can_answer && !r.mine && !r.on_break;
}

/**
 * Телефон — карточка во всю ширину с линией 3px сверху, кнопки сеткой в три; от 960px — одна
 * полоса над шапкой с линией 4px слева и кнопками справа.
 */
function GroupRsvpBanner({ group, rsvp }: { group: HubGroupDetails; rsvp: TrainingRsvpState }) {
  const next = group.next_training!;
  const r = rsvp.rsvp!;
  const relative = relativeLabel(next.date);
  const place = group.training_schedule?.place ?? next.place;

  return (
    <section
      aria-label="Are you coming to the next training?"
      className="mb-0 flex flex-col gap-2.5 border-b px-3.5 pb-3.5 pt-3 shadow-[inset_0_3px_0_var(--deep-live)] sm:mb-4 sm:rounded-[14px] sm:border min-[960px]:flex-row min-[960px]:items-center min-[960px]:gap-5 min-[960px]:px-[18px] min-[960px]:py-3.5 min-[960px]:shadow-[inset_4px_0_0_var(--deep-live)]"
      style={{ background: 'var(--deep-card-bg-raised)', borderColor: 'var(--deep-live-border)' }}
    >
      <div className="flex min-w-0 flex-col gap-1.5 min-[960px]:flex-row min-[960px]:items-center min-[960px]:gap-3">
        <div className="flex items-center gap-2">
          <span
            aria-hidden="true"
            className="h-2 w-2 flex-none rounded-full min-[960px]:h-2.5 min-[960px]:w-2.5"
            style={{ background: 'var(--deep-live)', boxShadow: '0 0 0 3px color-mix(in srgb, var(--deep-live) 14%, transparent)' }}
          />
          <span className="text-[9.5px] font-extrabold uppercase tracking-[.08em] min-[960px]:hidden" style={{ color: 'var(--deep-live)' }}>
            Next training{relative ? ` · ${relative}` : ''}
          </span>
          <span className="ml-auto text-[11px] font-bold min-[960px]:hidden" style={{ color: 'var(--deep-text-faint)' }}>
            {r.yes} going
          </span>
        </div>
        <div className="flex min-w-0 flex-col gap-[3px]">
          <span className="hidden text-[10px] font-extrabold uppercase tracking-[.08em] min-[960px]:inline" style={{ color: 'var(--deep-live)' }}>
            Next training{relative ? ` · ${relative}` : ''}
          </span>
          <div className="flex flex-wrap items-baseline gap-x-2 gap-y-0.5 min-[960px]:gap-x-2.5">
            <span className="hp-mono text-[15px] font-extrabold min-[960px]:text-[16px]" style={{ color: 'var(--deep-text)' }}>
              {formatNextDate(next.date)} · {next.start}
            </span>
            {place && (
              <span className="text-[12px] font-bold min-[960px]:text-[13px]" style={{ color: 'var(--deep-text-mute)' }}>
                📍 <bdi>{place}</bdi>
                <span className="hidden min-[960px]:inline"> · {r.yes} going</span>
              </span>
            )}
          </div>
        </div>
      </div>

      <span className="text-[13px] font-bold min-[960px]:ml-auto min-[960px]:text-[14px] min-[960px]:font-extrabold" style={{ color: 'var(--deep-text)' }}>
        Are you coming?
      </span>
      <div className="min-[960px]:flex-none">
        <RsvpButtons current={null} onAnswer={(a) => { void rsvp.answer(a); }} />
      </div>
      {rsvp.error && (
        <p className="m-0 text-[12px] font-extrabold" style={{ color: 'var(--deep-danger)' }}>{rsvp.error}</p>
      )}
    </section>
  );
}

export default GroupRsvpBanner;
export { showRsvpBanner };
