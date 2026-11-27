import React from 'react';
import { useTrainingsTab } from '../trainings-context';
import { TrainingsSection } from '../trainings-section';
import { FullPool, MiniPool } from '../next/lane-block';
import PastSessionHead from '../past/past-session-head';
import PastLevelWorkouts from '../past/past-level-workouts';

/**
 * Прошедшее занятие глазами УЧАСТНИКА — «только смотреть» (решение Влада 26.11.2026; отменяет «участнику
 * прошедших не показываем» CF9, docs/plans/trainings-coach-flow-plan.md). Открывается из его списка
 * «Past» (`past/past-sessions.tsx`), занятие — `deskSession` контекста (`?session=<id>`), ответы —
 * `selectedRoll`.
 *
 * Весь опубликованный план: все дорожки (своя подсвечена, если был в плане — сервер ставит зрителя на
 * дорожку и без ответа), под ними workout КАЖДОГО уровня с Print. Без счётчиков ответов, без
 * переклички и без кнопок ответа — сервер их и не даёт. Телефон — мини-бассейн, десктоп — полный.
 * Тренеру эта секция не рисуется (у него стол), в режиме «👁 View» — рисуется: экран участника.
 */
function MemberPastSessionSection() {
  const { deskSession, selectedRoll, closeDesk, isDesktop } = useTrainingsTab();
  if (!deskSession) return null;
  const rsvp = selectedRoll?.rsvp ?? null;
  const r = rsvp?.session_id === deskSession.id ? rsvp : null;
  const error = selectedRoll?.error ?? null;

  return (
    <div className="flex min-w-0 flex-col gap-3">
      <div className={isDesktop ? undefined : 'deep-panel-row'}>
        <button
          type="button"
          onClick={closeDesk}
          className="cursor-pointer border-0 bg-transparent p-0 text-[13px] font-extrabold"
          style={{ color: 'var(--t-text-3)' }}
        >
          {/* Открывают и из «Past» в Next, и из ленты Trainings (27.11.2026) — назад туда, откуда пришли. */}
          ← Back
        </button>
      </div>
      <TrainingsSection id="member-past-session" style={isDesktop ? { padding: 16 } : undefined}>
        <div className="flex min-w-0 flex-col gap-4">
          <PastSessionHead session={deskSession} desktop={isDesktop} />
          {r ? (
            <>
              {r.lane_view
                ? (isDesktop ? <FullPool r={r} past /> : <MiniPool r={r} past />)
                : (
                  <p className="m-0 text-[12.5px] font-bold" style={{ color: 'var(--t-text-3)' }}>
                    No published lane plan for this training.
                  </p>
                )}
              <PastLevelWorkouts r={r} />
            </>
          ) : (
            <p
              className="m-0 text-[12.5px] font-bold"
              style={{ color: error ? 'var(--deep-danger)' : 'var(--t-text-3)' }}
              role={error ? 'alert' : undefined}
            >
              {error ?? 'Loading…'}
            </p>
          )}
        </div>
      </TrainingsSection>
    </div>
  );
}

export default MemberPastSessionSection;
