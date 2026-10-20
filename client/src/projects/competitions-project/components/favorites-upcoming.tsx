import React from 'react';
import { useAuth } from '../../../hooks/useAuth';
import { useFavorites } from '../../../hooks/useFavorites';
import { routes } from '../../../utils/routes';
import { useUpcomingStarts } from '../../results-main-project/components/start-list/use-start-list';
import { formatApproxTime, swimLabel } from '../../results-main-project/components/start-list/start-list-helpers';

/*
 * Избранные пловцы залогиненного — ближайшие старты (С8.4). Живёт на /competitions,
 * а не на главной (вне скоупа — В9 говорит только про общий список соревнований).
 * Гость или нет стартов — блока нет.
 */
function FavoritesUpcoming() {
  const auth = useAuth();
  const { favoriteSwimmerIds } = useFavorites();
  const swimmerIds = React.useMemo(() => Array.from(favoriteSwimmerIds), [favoriteSwimmerIds]);
  const { data } = useUpcomingStarts(auth.isAuthenticated ? swimmerIds : []);

  if (!auth.isAuthenticated || !data || data.length === 0) return null;

  return (
    <section className="relative pt-[38px]" aria-label="Your favorites — upcoming starts">
      <h2 className="mb-[14px] text-[18px] font-black tracking-[-0.02em] text-[var(--t-text)]">
        Your favorites — upcoming starts
      </h2>
      <div className="flex flex-col gap-2">
        {data.map((s) => (
          <a
            key={s.id}
            href={`${routes.competitionUpcoming(s.org_comp_id)}?tab=startlist&swimmer=${s.swimmer_id}`}
            className="hp-card-std flex items-center justify-between gap-3 rounded-[12px] border border-[var(--t-border)] px-4 py-2.5 text-inherit no-underline"
          >
            <span className="text-[13px] font-bold">{s.swimmer_name}</span>
            <span className="text-[12px] text-[var(--t-text-2)]">
              {formatApproxTime(s.heat_start_at)} · {swimLabel(s.distance, s.style_name)}
            </span>
          </a>
        ))}
      </div>
    </section>
  );
}

export default FavoritesUpcoming;
