import React, { useState } from 'react';
import { routes } from '../../../utils/routes';
import DeepViewChips from '../../components/deep/view-chips';
import { GroupRecordsCard } from './group-cards';
import type { HubGroupDetails } from '../types';

/**
 * Список стартов группы — чип «≡ Results» таба Results (хендофф group-club-changes §3,
 * состав строки — решение Влада 28.09.2026): у каждого старта сколько пловцов группы плыло,
 * сколько рекордов группы там поставлено и сколько медалей.
 *
 * Строка ведёт в протокол ЭТОГО старта (многодневка — весь турнир через `eventId`), а
 * карточка целиком — на экран результатов группы `/groups/{slug}/results`: там все заплывы
 * ростера с фильтрами. Отдельной строкой туда не ведём — экран не умеет открываться на одном
 * старте, и двадцать одинаковых ссылок читались бы как сломанные.
 *
 * Цифры считает сервер (`competitions` в ответе группы): дни многодневки сложены, эстафеты
 * по членству, медали — по единому правилу продукта. Здесь ничего не пересчитывается.
 */

/** Сколько стартов видно сразу; остальные — по кнопке. */
const INITIAL = 12;

function GroupCompetitionsCard({ group }: { group: HubGroupDetails }) {
  const [expanded, setExpanded] = useState(false);
  const list = group.competitions ?? [];
  const shown = expanded ? list : list.slice(0, INITIAL);
  const real = !group.is_virtual && group.id > 0;

  return (
    <section className="deep-card mb-4">
      <div className="flex flex-wrap items-baseline justify-between gap-x-4 gap-y-1">
        <div>
          <div className="deep-card-title">Competitions</div>
          <div className="deep-card-sub mt-1">
            {list.length} {list.length === 1 ? 'meet' : 'meets'} of the roster · newest first
          </div>
        </div>
        {real && list.length > 0 && (
          <a
            href={routes.groupResults(group.slug)}
            className="hp-mono text-[12.5px] font-extrabold no-underline"
            style={{ color: 'var(--deep-accent)' }}
          >
            All group results →
          </a>
        )}
      </div>

      {list.length === 0 ? (
        <div className="mt-4 text-[13px] font-bold" style={{ color: 'var(--deep-text-mute)' }}>
          The roster has not swum any competition yet.
        </div>
      ) : (
        <div className="mt-4 flex flex-col gap-1.5">
          {shown.map((c) => {
            const medals = c.golds + c.silvers + c.bronzes;
            const dates = c.date_from === c.date_to ? c.date_to : `${c.date_from} – ${c.date_to}`;
            return (
              <a
                key={`${c.event_id ?? 'c'}-${c.competition_id}`}
                href={routes.competitionSwims(c.competition_id, { eventId: c.event_id ?? null })}
                className="flex items-center justify-between gap-3 px-3 py-2.5 no-underline"
                style={{ background: 'var(--deep-card-bg-row)', borderRadius: 'var(--deep-radius-row)' }}
              >
                <div className="min-w-0">
                  {/* dir="auto" — только на тексте: названия стартов бывают ивритскими. */}
                  <div dir="auto" className="truncate text-left text-[13px] font-extrabold" style={{ color: 'var(--deep-text)' }}>
                    {c.name}
                  </div>
                  <div className="hp-mono truncate text-[11px] font-bold" style={{ color: 'var(--deep-text-mute)' }}>
                    {dates} · {c.swimmers} {c.swimmers === 1 ? 'swimmer' : 'swimmers'} · {c.swims} {c.swims === 1 ? 'swim' : 'swims'}
                  </div>
                </div>
                <div className="hp-mono flex shrink-0 flex-col items-end gap-0.5 text-[11.5px] font-extrabold">
                  {medals > 0 && (
                    <span style={{ color: 'var(--deep-text)' }} title={`${c.golds} gold · ${c.silvers} silver · ${c.bronzes} bronze`}>
                      {c.golds > 0 && <>🥇{c.golds} </>}
                      {c.silvers > 0 && <>🥈{c.silvers} </>}
                      {c.bronzes > 0 && <>🥉{c.bronzes}</>}
                    </span>
                  )}
                  {c.records > 0 && (
                    <span style={{ color: 'var(--deep-accent)' }} title="Group records set at this meet that still stand">
                      {c.records} {c.records === 1 ? 'record' : 'records'}
                    </span>
                  )}
                </div>
              </a>
            );
          })}
        </div>
      )}

      {list.length > INITIAL && (
        <button
          type="button"
          onClick={() => setExpanded((v) => !v)}
          className="hp-mono mt-3 cursor-pointer border-0 bg-transparent p-0 text-[12.5px] font-extrabold"
          style={{ color: 'var(--deep-accent)' }}
        >
          {expanded ? 'Show fewer' : `Show all ${list.length} →`}
        </button>
      )}
    </section>
  );
}

/** Вид таба Results (`?view=`): старты, рекорды группы, лучшие за сезон. */
export type GroupResultsView = 'results' | 'records' | 'season-best';
export const GROUP_RESULTS_VIEWS: readonly GroupResultsView[] = ['results', 'records', 'season-best'];

/**
 * Таб Results группы: чипы как у пловца (`DeepViewChips`), под ними одна карточка вида.
 * Records и Season bests были отдельными табами (Records) или не было вовсе (Season bests);
 * хендофф свёл их сюда, чтобы ряд табов на телефоне остался в шесть колонок.
 */
function GroupResultsTab({
  group, view, onView,
}: {
  group: HubGroupDetails;
  view: GroupResultsView;
  onView: (next: GroupResultsView) => void;
}) {
  // Последняя карточка несёт свой mb-4 — внутри панели он лишний (как у EntityPanel).
  return (
    <div className="[&>*:last-child]:mb-0">
      <DeepViewChips<GroupResultsView>
        ariaLabel="Results view"
        active={view}
        onSelect={onView}
        chips={[
          { id: 'results', icon: '≡', label: 'Results', caption: 'meets and swims of the group, newest first' },
          {
            id: 'records', icon: '🏅', label: 'Records', badge: group.bests.length,
            caption: 'best time of the group per event · both pools',
          },
          {
            id: 'season-best', icon: '☀', label: 'Season bests',
            caption: `best time per event this season${group.season_label ? ` · ${group.season_label}` : ''}`,
          },
        ]}
      />
      {view === 'records' && <GroupRecordsCard group={group} />}
      {view === 'season-best' && <GroupRecordsCard group={group} season />}
      {view === 'results' && <GroupCompetitionsCard group={group} />}
    </div>
  );
}

export default GroupCompetitionsCard;
export { GroupResultsTab };
