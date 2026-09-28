import React from 'react';
import type { RsvpAnswer, RsvpNote, TrainingRsvp, TrainingRsvpPerson } from '../types';

/**
 * Кирпичи ответов «иду / не уверен / не приду» (хендофф group-club-changes §4, вариант 2a;
 * план docs/plans/entity-hero-roles-plan.md, Ш2): полоса со счётчиками, три кнопки, строка
 * тренера (аватары + «Who's coming →») и список людей для таба Trainings.
 *
 * Цвета — роли темы: иду `--deep-live`, не уверен `--deep-gold-bar` / `--deep-gold`, не приду
 * `--deep-danger`. Текст на ЗАЛИТОЙ кнопке — `--deep-accent-ink`: в тёмной теме цвета светлые,
 * и белый текст на них не читается, а ink там тёмный.
 */

interface Tone {
  /** Текст и рамка кнопки в покое. */
  fg: string;
  border: string;
  /** Заливка нажатой кнопки и сегмента полосы. */
  fill: string;
}

const TONE: Record<RsvpAnswer, Tone> = {
  yes: { fg: 'var(--deep-live)', border: 'var(--deep-live-border)', fill: 'var(--deep-live)' },
  maybe: { fg: 'var(--deep-gold)', border: 'var(--deep-gold-border)', fill: 'var(--deep-gold-bar)' },
  no: {
    fg: 'var(--deep-danger)',
    border: 'color-mix(in srgb, var(--deep-danger) 30%, transparent)',
    fill: 'var(--deep-danger)',
  },
};

const BUTTON_LABEL: Record<RsvpAnswer, string> = { yes: '✓ Going', maybe: '? Not sure', no: "✕ Can't come" };
const COUNT_LABEL: Record<RsvpAnswer, string> = { yes: 'going', maybe: 'not sure', no: 'not going' };
const NOTE_LABEL: Record<RsvpNote, string> = {
  late: 'late ~10 min', 'first-hour': '1st hour only', 'leaving-early': 'leaving early',
};
const ORDER: RsvpAnswer[] = ['yes', 'maybe', 'no'];

/**
 * Полоса 6px + счётчики. Ширина сегмента = ответы / участники; серый остаток — не ответили
 * (подписи «no answer» нет — хендофф). Сегмент «не приду» приглушён (75%), чтобы полоса
 * читалась про пришедших.
 */
function RsvpBar({ rsvp }: { rsvp: TrainingRsvp }) {
  const pct = (n: number) => (rsvp.total > 0 ? `${(n / rsvp.total) * 100}%` : '0%');
  // Телефон — полоса, под ней счётчики (2a); от 960px — в одну строку (4a).
  return (
    <div className="flex flex-col gap-1.5 min-[960px]:flex-row min-[960px]:items-center min-[960px]:gap-4">
      <div
        className="flex h-1.5 overflow-hidden rounded-[3px] min-[960px]:min-w-0 min-[960px]:flex-1"
        style={{ background: 'var(--deep-divider)' }}
        role="img"
        aria-label={`${rsvp.yes} going, ${rsvp.maybe} not sure, ${rsvp.no} not going of ${rsvp.total}`}
      >
        {ORDER.map((a) => (
          <span
            key={a}
            style={{ width: pct(rsvp[a]), background: TONE[a].fill, opacity: a === 'no' ? 0.75 : 1 }}
          />
        ))}
      </div>
      <div className="hp-mono flex flex-wrap items-center gap-x-3 gap-y-1 text-[11.5px] font-extrabold min-[960px]:flex-none min-[960px]:text-[12px]">
        {ORDER.map((a) => (
          <span key={a} className="flex items-center gap-[5px]" style={{ color: TONE[a].fg }}>
            <span aria-hidden="true" className="h-[7px] w-[7px] rounded-full" style={{ background: TONE[a].fill }} />
            {rsvp[a]} {COUNT_LABEL[a]}
          </span>
        ))}
      </div>
    </div>
  );
}

/**
 * Три кнопки ответа. Нажатая остаётся залитой; повторный тап по ней снимает ответ
 * (`onAnswer(null)`). `size`: `hero` — сетка на всю ширину на телефоне и ряд справа на
 * десктопе; `compact` — строка человека в списке тренера.
 */
function RsvpButtons({
  current, onAnswer, disabled, size = 'hero',
}: {
  current: RsvpAnswer | null;
  onAnswer: (next: RsvpAnswer | null) => void;
  disabled?: boolean;
  size?: 'hero' | 'compact';
}) {
  const hero = size === 'hero';
  return (
    <div
      className={hero
        ? 'grid grid-cols-3 gap-1.5 min-[960px]:flex min-[960px]:gap-1.5'
        : 'flex gap-1'}
      role="group"
      aria-label="Are you coming?"
    >
      {ORDER.map((a) => {
        const on = current === a;
        const t = TONE[a];
        return (
          <button
            key={a}
            type="button"
            disabled={disabled}
            aria-pressed={on}
            onClick={() => onAnswer(on ? null : a)}
            title={hero ? undefined : BUTTON_LABEL[a]}
            className={hero
              ? 'h-10 cursor-pointer rounded-[10px] border text-[12.5px] font-extrabold disabled:cursor-default disabled:opacity-50 min-[960px]:h-[38px] min-[960px]:px-3.5'
              : 'hp-mono h-7 w-8 cursor-pointer rounded-[8px] border text-[12px] font-extrabold disabled:cursor-default disabled:opacity-50'}
            style={on
              ? { background: t.fill, borderColor: t.fill, color: 'var(--deep-accent-ink)' }
              : { background: 'var(--deep-card-bg)', borderColor: t.border, color: t.fg }}
          >
            {hero ? BUTTON_LABEL[a] : BUTTON_LABEL[a].slice(0, 1)}
          </button>
        );
      })}
    </div>
  );
}

/** Кружок-инициал человека; цвет — по полу привязанного пловца. */
function RsvpAvatar({ person, size = 24, ring = true }: { person: TrainingRsvpPerson; size?: number; ring?: boolean }) {
  const bg = person.gender === 'female' ? 'var(--deep-female)' : person.gender === 'male' ? 'var(--deep-male)' : 'var(--deep-text-mute)';
  return (
    <span
      aria-hidden="true"
      className="flex flex-none items-center justify-center rounded-full text-[10px] font-extrabold"
      style={{
        width: size, height: size, background: bg, color: 'var(--deep-accent-ink)',
        border: ring ? '2px solid var(--deep-card-bg)' : undefined,
      }}
    >
      {(person.name.trim()[0] ?? '?').toUpperCase()}
    </span>
  );
}

/**
 * Строка тренера в шапке: стопка аватаров идущих, «+N» и «Who's coming →» (фиолетовый —
 * инструмент управляющего). Своих кнопок у тренера здесь нет (хендофф) — если он сам в
 * составе, кнопки рисуются отдельно.
 */
function RsvpStaffRow({ rsvp, onOpen }: { rsvp: TrainingRsvp; onOpen: () => void }) {
  const going = (rsvp.people ?? []).filter((p) => p.answer === 'yes');
  const shown = going.slice(0, 5);
  return (
    <div className="flex items-center gap-2">
      <div className="flex">
        {shown.map((p) => (
          <span key={p.user_id} className="-mr-1.5"><RsvpAvatar person={p} /></span>
        ))}
      </div>
      {going.length > shown.length && (
        <span className="ml-2 text-[11.5px] font-bold" style={{ color: 'var(--deep-text-mute)' }}>
          +{going.length - shown.length}
        </span>
      )}
      <button
        type="button"
        onClick={onOpen}
        className="ml-auto h-[30px] cursor-pointer rounded-[9px] border px-2.5 text-[12px] font-extrabold min-[960px]:h-[34px] min-[960px]:px-3 min-[960px]:text-[12.5px]"
        style={{ borderColor: 'var(--deep-ow-border)', background: 'var(--deep-ow-chip)', color: 'var(--deep-ow)' }}
      >
        Who&apos;s coming →
      </button>
    </div>
  );
}

const GROUP_TITLE: Record<RsvpAnswer | 'none', string> = {
  yes: 'Going', maybe: 'Not sure', no: "Can't come", none: 'No answer',
};

/**
 * Список «кто идёт» для управляющего (таб Trainings → Sessions): группы Going / Not sure /
 * Can't come / No answer; у каждого — три маленькие кнопки, тренер ставит ответ за человека.
 * Ответ, поставленный тренером, помечен «by coach».
 */
function RsvpPeopleList({
  rsvp, onAnswerFor, disabled,
}: {
  rsvp: TrainingRsvp;
  onAnswerFor: (userId: number, next: RsvpAnswer | null) => void;
  disabled?: boolean;
}) {
  const people = rsvp.people ?? [];
  const groups: Array<RsvpAnswer | 'none'> = ['yes', 'maybe', 'no', 'none'];
  if (people.length === 0) {
    return (
      <p className="m-0 text-[13px] font-bold" style={{ color: 'var(--deep-text-mute)' }}>
        The group has no member accounts yet — people answer after they join the group.
      </p>
    );
  }
  return (
    <div className="flex flex-col gap-3">
      {groups.map((g) => {
        const rows = people.filter((p) => (p.answer ?? 'none') === g);
        if (rows.length === 0) return null;
        return (
          <div key={g}>
            <div
              className="mb-1.5 text-[10px] font-extrabold uppercase tracking-[.08em]"
              style={{ color: g === 'none' ? 'var(--deep-text-mute)' : TONE[g].fg }}
            >
              {GROUP_TITLE[g]} · {rows.length}
            </div>
            <div className="flex flex-col gap-1">
              {rows.map((p) => (
                <div
                  key={p.user_id}
                  className="flex items-center gap-2.5 px-2.5 py-1.5"
                  style={{ background: 'var(--deep-card-bg-row)', borderRadius: 'var(--deep-radius-row)' }}
                >
                  <RsvpAvatar person={p} size={26} ring={false} />
                  <div className="min-w-0 flex-1">
                    <div dir="auto" className="truncate text-left text-[13px] font-extrabold" style={{ color: 'var(--deep-text)' }}>
                      {p.name}
                    </div>
                    {(p.note || p.set_by_coach) && (
                      <div className="truncate text-[11px] font-bold" style={{ color: 'var(--deep-text-mute)' }}>
                        {[p.note ? NOTE_LABEL[p.note] : null, p.set_by_coach ? 'by coach' : null].filter(Boolean).join(' · ')}
                      </div>
                    )}
                  </div>
                  <RsvpButtons
                    size="compact"
                    current={p.answer}
                    disabled={disabled}
                    onAnswer={(next) => onAnswerFor(p.user_id, next)}
                  />
                </div>
              ))}
            </div>
          </div>
        );
      })}
    </div>
  );
}

/** Чип липкой полосы: ответ участника / «going?» / счётчики тренеру (хендофф §5). */
function rsvpStickyChip(rsvp: TrainingRsvp, dayLabel: string): { label: string; style: React.CSSProperties } {
  if (rsvp.can_manage) {
    return {
      label: `${rsvp.yes} ✓ · ${rsvp.maybe} ? · ${rsvp.no} ✕`,
      style: { borderColor: 'var(--deep-ow-border)', background: 'var(--deep-ow-chip)', color: 'var(--deep-ow)' },
    };
  }
  const a = rsvp.mine?.answer;
  if (!a) {
    return {
      label: `${dayLabel} ${rsvp.start} · going?`,
      style: { borderColor: 'var(--deep-live-border)', background: 'var(--deep-card-bg)', color: 'var(--deep-live)' },
    };
  }
  const label = a === 'yes' ? `✓ Going · ${dayLabel} ${rsvp.start}` : a === 'maybe' ? `? Not sure · ${dayLabel}` : `✕ Can't · ${dayLabel}`;
  return { label, style: { borderColor: TONE[a].fill, background: TONE[a].fill, color: 'var(--deep-accent-ink)' } };
}

export { RsvpBar, RsvpButtons, RsvpStaffRow, RsvpPeopleList, rsvpStickyChip };
