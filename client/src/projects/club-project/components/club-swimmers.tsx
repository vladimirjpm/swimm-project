import React, { useEffect, useState } from 'react';
import { useClubRoster } from '../../../hooks/useClubRoster';
import { routes } from '../../../utils/routes';
import ClubAvatar from './club-avatar';

/**
 * Swimmers — ростер клуба (GET /api/clubs/{id}/roster), узкий список ≤440px
 * (CARDS.md §6, club-page-cards-sonnet.md §4.4). Возрастные пилюли — НЕ зачётные группы
 * лестницы (у них свои границы 8–11/12–14/15–16/17+), к `group` из скоупа отношения не имеют;
 * сезон в запрос идёт из скоупа, если выбран.
 */

interface Props {
  clubId: number;
  /** Сезон из глобального скоупа страницы; null — без ограничения. */
  season: number | null;
}

interface AgeBucket {
  key: string;
  label: string;
  ageFrom: number | null;
  ageTo: number | null;
}

const AGE_BUCKETS: AgeBucket[] = [
  { key: 'all', label: 'All', ageFrom: null, ageTo: null },
  { key: '8-11', label: '8–11', ageFrom: 8, ageTo: 11 },
  { key: '12-14', label: '12–14', ageFrom: 12, ageTo: 14 },
  { key: '15-16', label: '15–16', ageFrom: 15, ageTo: 16 },
  { key: '17+', label: '17+', ageFrom: 17, ageTo: null },
];

/**
 * Счётчики для возрастных пилюль. У API нет отдельной «totals по бакетам» ручки (K4.2 закрыт,
 * серверный код вне скоупа этого таска) — считаем `total` пятью лёгкими запросами
 * (pageSize=1, нужен только total), с учётом текущего фильтра пола/сезона.
 */
function useAgeBucketCounts(
  clubId: number | null,
  gender: 'male' | 'female' | null,
  season: number | null,
): Record<string, number> {
  const [counts, setCounts] = useState<Record<string, number>>({});

  useEffect(() => {
    if (clubId == null) {
      setCounts({});
      return;
    }

    let cancelled = false;

    Promise.all(
      AGE_BUCKETS.map((b) => {
        const qs = new URLSearchParams();
        qs.set('page', '1');
        qs.set('pageSize', '1');
        if (gender != null) qs.set('gender', gender);
        if (b.ageFrom != null) qs.set('ageFrom', String(b.ageFrom));
        if (b.ageTo != null) qs.set('ageTo', String(b.ageTo));
        if (season != null) qs.set('season', String(season));

        return fetch(`/api/clubs/${clubId}/roster?${qs.toString()}`)
          .then((res) => (res.ok ? (res.json() as Promise<{ total: number }>) : { total: 0 }))
          .then((json) => [b.key, json.total] as const)
          .catch(() => [b.key, 0] as const);
      }),
    ).then((pairs) => {
      if (!cancelled) setCounts(Object.fromEntries(pairs));
    });

    return () => {
      cancelled = true;
    };
  }, [clubId, gender, season]);

  return counts;
}

function ClubSwimmers({ clubId, season }: Props) {
  const [ageKey, setAgeKey] = useState<string>('all');
  const [gender, setGender] = useState<'male' | 'female' | null>(null);

  const bucket = AGE_BUCKETS.find((b) => b.key === ageKey) ?? AGE_BUCKETS[0];
  const counts = useAgeBucketCounts(clubId, gender, season);
  const { items, total, hasMore, loading, loadMore } = useClubRoster(clubId, {
    gender,
    ageFrom: bucket.ageFrom,
    ageTo: bucket.ageTo,
    season,
  });

  return (
    <section className="deep-card mb-4">
      <div className="deep-card-title">Swimmers</div>

      <div className="mt-3 flex flex-wrap items-center gap-2">
        {AGE_BUCKETS.map((b) => (
          <button
            key={b.key}
            type="button"
            className={`deep-pill ${ageKey === b.key ? 'deep-pill--active' : ''}`}
            onClick={() => setAgeKey(b.key)}
          >
            {b.label}
            {counts[b.key] != null && <span> ({counts[b.key]})</span>}
          </button>
        ))}
        <span className="mx-1 h-4 w-px" style={{ background: 'var(--deep-divider)' }} />
        <button
          type="button"
          className={`deep-pill ${gender === 'male' ? 'deep-pill--active' : ''}`}
          onClick={() => setGender((g) => (g === 'male' ? null : 'male'))}
        >
          ♂
        </button>
        <button
          type="button"
          className={`deep-pill ${gender === 'female' ? 'deep-pill--active' : ''}`}
          onClick={() => setGender((g) => (g === 'female' ? null : 'female'))}
        >
          ♀
        </button>
      </div>

      <div className="mx-auto mt-3 flex max-w-[440px] flex-col gap-1">
        {items.length === 0 && !loading ? (
          <div className="text-[13px] font-bold" style={{ color: 'var(--deep-text-mute)' }}>
            No swimmers match this filter
          </div>
        ) : (
          items.map((s) => (
            <a
              key={s.swimmer_id}
              href={routes.swimmer(s.swimmer_id)}
              className="flex items-center gap-3 rounded-[var(--deep-radius-row)] px-2 py-2 no-underline"
              style={{ background: 'var(--deep-card-bg-row)' }}
            >
              <ClubAvatar firstName={s.first_name} lastName={s.last_name} gender={s.gender} size={28} />
              <div
                className="min-w-0 flex-1 truncate text-[13px] font-extrabold"
                style={{ color: 'var(--deep-text)' }}
              >
                {`${s.first_name} ${s.last_name}`.trim() || '—'}
              </div>
              <div className="shrink-0 text-[11px] font-bold" style={{ color: 'var(--deep-text-mute)' }}>
                age {s.age}
              </div>
              <div className="shrink-0 text-[11px] font-bold" style={{ color: 'var(--deep-text-mute)' }}>
                {s.competitions} comps
              </div>
            </a>
          ))
        )}
      </div>

      {hasMore && (
        <button
          type="button"
          onClick={loadMore}
          disabled={loading}
          className="mx-auto mt-3 block w-full max-w-[440px] rounded-[10px] py-2 text-[12px] font-extrabold"
          style={{
            background: 'var(--deep-card-bg-raised)',
            color: 'var(--deep-text-mute)',
            border: '1px solid var(--deep-card-border)',
          }}
        >
          {loading ? 'Loading…' : `Show all ${total}`}
        </button>
      )}
    </section>
  );
}

export default ClubSwimmers;
