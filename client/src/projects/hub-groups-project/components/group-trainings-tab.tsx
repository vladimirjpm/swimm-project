import React, { useState } from 'react';
import { routes } from '../../../utils/routes';
import { readViewParam, writeViewParam } from '../../components/deep/view-chips';
import GroupLanes from './group-lanes';
import { RsvpBar, RsvpButtons, RsvpPeopleList } from './group-rsvp';
import { formatNextDate } from './group-training-slots';
import type { TrainingRsvpState } from '../use-training-rsvp';
import type { HubGroupDetails } from '../types';

/**
 * Таб Trainings: переключатель `Sessions | Lanes` (хендофф group-club-changes §3).
 *
 * Lanes был отдельным табом и делал ряд на телефоне семиколоночным (~45px на колонку).
 * План дорожек — про те же занятия, что и журнал тренировок, поэтому он живёт здесь же.
 * Вид — в `?view=` (как чипы Results), по умолчанию Sessions без параметра.
 *
 * Sessions — ближайшее занятие со списком «кто идёт» (Ш2) и вход в журнал тренировок (сама
 * таблица живёт на ДРУГОМ экране, `/groups/{slug}/results?tab=trainings`). Сюда ведёт
 * «Who's coming →» из шапки. Управляющему — все участники по группам ответа, и он ставит
 * ответ за человека; участнику — счётчики и свои кнопки. Вид по дорожкам (3b) — этап Ш3.
 *
 * Сегмент — акцентный у участника и фиолетовый у управляющего: у того здесь правка
 * (роль «можешь менять», §3).
 */

type TrainingsView = 'sessions' | 'lanes';
const VIEWS: readonly TrainingsView[] = ['sessions', 'lanes'];

function GroupTrainingsTab({
  group, manages, rsvp,
}: {
  group: HubGroupDetails;
  manages: boolean;
  rsvp?: TrainingRsvpState | null;
}) {
  const [view, setView] = useState<TrainingsView>(() => readViewParam(VIEWS, 'sessions'));
  const pick = (next: TrainingsView) => { setView(next); writeViewParam(next, 'sessions'); };
  const activeBg = manages ? 'var(--deep-ow)' : 'var(--deep-accent)';

  return (
    <div className="[&>*:last-child]:mb-0">
      <div className="deep-panel-row mb-3">
        <div
          className="inline-flex gap-0.5 rounded-[10px] p-[3px]"
          style={{ background: 'var(--deep-divider)' }}
          role="group"
          aria-label="Trainings view"
        >
          {([['sessions', 'Sessions'], ['lanes', 'Lanes']] as const).map(([id, label]) => (
            <button
              key={id}
              type="button"
              aria-pressed={view === id}
              onClick={() => pick(id)}
              className="cursor-pointer rounded-[8px] border-0 px-3.5 py-1.5 text-[12px] font-extrabold min-[960px]:px-4 min-[960px]:py-[7px] min-[960px]:text-[12.5px]"
              style={view === id
                ? { background: activeBg, color: 'var(--deep-accent-ink)' }
                : { background: 'transparent', color: 'var(--deep-text-mute)' }}
            >
              {label}
            </button>
          ))}
        </div>
      </div>

      {view === 'lanes' ? (
        <GroupLanes groupId={group.id} manages={manages} />
      ) : (
        <>
          <NextSessionCard group={group} rsvp={rsvp} />
          <section className="deep-card mb-0">
            <div className="deep-card-title">Training log</div>
            <div className="deep-card-sub mt-1">private — group members and admins</div>
            <a
              href={`${routes.groupResults(group.slug)}?tab=trainings`}
              className="hp-mono mt-4 inline-block rounded-[10px] border px-4 py-2 text-[13px] font-extrabold no-underline"
              style={{
                borderColor: 'var(--deep-accent-border)',
                background: 'var(--deep-accent-chip)',
                color: 'var(--deep-accent)',
              }}
            >
              🔒 Open training log →
            </a>
          </section>
        </>
      )}
    </div>
  );
}

/**
 * Ближайшее занятие с ответами. Нет расписания или ответы не приехали — карточки нет: журнал
 * тренировок ниже остаётся.
 */
function NextSessionCard({ group, rsvp }: { group: HubGroupDetails; rsvp?: TrainingRsvpState | null }) {
  const next = group.next_training;
  const r = rsvp?.rsvp;
  if (!next || !r || r.session_id !== next.id) return null;

  const place = group.training_schedule?.place ?? next.place;
  return (
    <section className="deep-card mb-4">
      <div className="flex flex-wrap items-baseline justify-between gap-x-4 gap-y-1">
        <div className="deep-card-title">Next training</div>
        <div className="hp-mono text-[12.5px] font-extrabold" style={{ color: 'var(--deep-text)' }}>
          {formatNextDate(next.date)} · {next.start}{next.end ? `–${next.end}` : ''}
          {place && <span style={{ color: 'var(--deep-text-mute)' }}> · <bdi>{place}</bdi></span>}
        </div>
      </div>

      {r.total > 0 && <div className="mt-3"><RsvpBar rsvp={r} /></div>}

      {r.is_member && r.can_answer && (
        <div className="mt-4">
          <div className="mb-1.5 text-[10px] font-extrabold uppercase tracking-[.08em]" style={{ color: 'var(--deep-text-mute)' }}>
            Your answer{r.mine?.set_by_coach ? ' · set by coach' : ''}
          </div>
          <RsvpButtons current={r.mine?.answer ?? null} onAnswer={(a) => { void rsvp!.answer(a); }} />
        </div>
      )}

      {r.can_manage && (
        <div className="mt-4">
          <RsvpPeopleList
            rsvp={r}
            disabled={!r.can_answer}
            onAnswerFor={(userId, a) => { void rsvp!.answerFor(userId, a); }}
          />
        </div>
      )}

      {rsvp?.error && (
        <p className="mb-0 mt-3 text-[12px] font-extrabold" style={{ color: 'var(--deep-danger)' }}>{rsvp.error}</p>
      )}
    </section>
  );
}

export default GroupTrainingsTab;
