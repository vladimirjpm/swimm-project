import React from 'react';
import type { CompetitionOverview } from '../types';
import { useAppSelector } from '../../../../../store/store';
import { useFavoritesContext } from '../../../../../hooks/favorites-context';
import HelperSwimmer from '../../../../../utils/helpers/helper-swimmer';
import { initials } from './module-defs';
import UI_SwimmerNameCell from '../../../../components/mix/swimmer-name-cell/swimmer-name-cell';

// Карточка модуля Favorites (9d, шестой модуль, только залогиненному). Спека dc.html
// секция 9d (sc-if is9dFavs), композиция 1:1. Данные — не через props, а из готовой
// логики: useFavoritesContext (favorites/primarySwimmerId/favoriteSwimmerIds) + результаты
// из useAppSelector((s) => s.dataSourceSelected).results, матчинг ТОЛЬКО через
// HelperSwimmer.resultBelongsToSwimmer (эстафеты по составу ног, см. competition-personal-strip.tsx).
//
// ПРАВИЛА КАРТОЧКИ — docs/competition-overview-cards.md (раздел Favorites).

interface Props {
  overview: CompetitionOverview;
  onOpenSwimsScoped?(scope: 'my' | 'favorites'): void;
  onOpenClub?(club: string): void;
}

export default function ModuleCardFavorites({ onOpenSwimsScoped, onOpenClub }: Props) {
  const { favorites, primarySwimmerId, favoriteSwimmerIds } = useFavoritesContext();
  const results = useAppSelector((s) => s.dataSourceSelected)?.results ?? [];

  // Матчинг — только через HelperSwimmer.resultBelongsToSwimmer (эстафеты по составу ног).
  const matches = (r: any, id: number) => HelperSwimmer.resultBelongsToSwimmer(r, id);

  // Избранные пловцы (без primary — у него своя карточка ⭐ в персональной полосе).
  const favSwimmerIds = [...favoriteSwimmerIds].filter((id) => id !== primarySwimmerId);

  const swimmerCards = favSwimmerIds
    .map((id) => {
      const swims = results.filter((r: any) => matches(r, id));
      if (swims.length === 0) return null;
      const fav = favorites.find((f) => f.target_type === 'swimmer' && f.swimmer_id === id);
      const best = swims.reduce(
        (b: any, r: any) => (r.international_points > (b?.international_points ?? -1) ? r : b),
        null,
      );
      const medals = {
        gold: swims.filter((r: any) => Number(r.position) === 1).length,
        silver: swims.filter((r: any) => Number(r.position) === 2).length,
        bronze: swims.filter((r: any) => Number(r.position) === 3).length,
      };
      return {
        id,
        firstName: best?.first_name ?? fav?.swimmer_name ?? '—',
        lastName: best?.last_name ?? '',
        firstNameEn: best?.first_name_en ?? '',
        lastNameEn: best?.last_name_en ?? '',
        club: best?.club ?? '',
        ageGroup: best?.age_group ?? '',
        best,
        medals,
        count: swims.length,
      };
    })
    .filter((v): v is NonNullable<typeof v> => v != null);

  const favClubs = favorites.filter((f) => f.target_type === 'club');

  const favSwims = results.filter((r: any) => favSwimmerIds.some((id) => matches(r, id)));
  const favHere = swimmerCards.length;
  const hasAnything = swimmerCards.length > 0 || favClubs.length > 0;

  return (
    <section className="ov2-card" data-module="favorites">
      <div className="mb-3 flex items-baseline gap-2.5 flex-wrap">
        <span className="ov2-card__title">❤️ Favorites</span>
        <span className="text-[12px] font-semibold" style={{ color: 'var(--theme-mode-text-muted)' }}>
          only for you · {favHere} swimmers
        </span>
        <span className="flex-1" />
        <button type="button" onClick={() => onOpenSwimsScoped?.('favorites')} className="ov2-card__link">
          All favorite swims →
        </button>
      </div>

      {hasAnything ? (
        <>
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-2.5">
            {swimmerCards.map((c) => (
              <button
                key={c.id}
                type="button"
                onClick={() => onOpenSwimsScoped?.('favorites')}
                className="flex min-w-0 items-center gap-2.5 rounded-[11px] p-2.5 text-left"
                style={{
                  background: 'color-mix(in srgb, var(--m) 7%, transparent)',
                  border: '1px solid color-mix(in srgb, var(--m) 28%, transparent)',
                  color: 'var(--theme-mode-text)',
                }}
              >
                <span className="flex min-w-0 flex-1 flex-col gap-px">
                  <span className="flex min-w-0 items-center gap-1.5">
                    <UI_SwimmerNameCell
                      firstName={c.firstName}
                      lastName={c.lastName}
                      firstNameEn={c.firstNameEn}
                      lastNameEn={c.lastNameEn}
                      club={c.club}
                      showClubIcon
                      clubIconSide="left"
                      clubIconWidth="10"
                      firstLineClassName="truncate text-[14px] font-extrabold"
                      secondLineClassName="truncate text-[11.5px] font-semibold text-[var(--theme-mode-text-secondary)]"
                      className="min-w-0 flex-1"
                    />
                    {c.ageGroup && (
                      <span
                        className="flex-none rounded-full px-2 py-px text-[10px] font-extrabold"
                        style={{ background: 'color-mix(in srgb, var(--m) 12%, transparent)', color: 'var(--m-solid)' }}
                      >
                        {c.ageGroup}
                      </span>
                    )}
                  </span>
                  {(c.medals.gold > 0 || c.medals.silver > 0 || c.medals.bronze > 0) && (
                    <span className="text-[11.5px] font-bold">
                      {c.medals.gold > 0 && `🥇${c.medals.gold} `}
                      {c.medals.silver > 0 && `🥈${c.medals.silver} `}
                      {c.medals.bronze > 0 && `🥉${c.medals.bronze}`}
                    </span>
                  )}
                </span>
                <span className="flex flex-none flex-col items-end gap-px">
                  <span className="text-[13px] font-black" style={{ fontVariantNumeric: 'tabular-nums' }}>
                    {c.best?.time ?? '—'}
                  </span>
                  {c.best && (
                    <span className="text-[10.5px] font-semibold" style={{ color: 'var(--theme-mode-text-muted)' }}>
                      {c.best.distance}m {c.best.style_name}
                      {c.best.international_points ? ` · ${c.best.international_points} pts` : ''}
                    </span>
                  )}
                  <span className="text-[11px] font-bold" style={{ color: 'var(--m-solid)' }}>
                    {c.count} swims
                  </span>
                </span>
              </button>
            ))}

            {favClubs.map((f) => (
              <button
                key={`club-${f.id}`}
                type="button"
                onClick={() => onOpenClub?.(f.club_name ?? '')}
                className="flex min-w-0 items-center gap-2.5 rounded-[11px] p-2.5 text-left"
                style={{
                  background: 'color-mix(in srgb, var(--theme-personal-accent) 6%, transparent)',
                  border: '1px solid color-mix(in srgb, var(--theme-personal-accent) 20%, transparent)',
                  color: 'var(--theme-mode-text)',
                }}
              >
                <span className="ov2-logo h-[38px] w-[38px] flex-none text-[10px]">
                  {initials(f.club_name ?? '?')}
                </span>
                <span className="flex min-w-0 flex-1 flex-col gap-px">
                  <span className="text-[10px] font-extrabold uppercase tracking-[.06em]" style={{ color: 'var(--m-solid)' }}>
                    CLUB
                  </span>
                  <span dir="auto" className="truncate text-[14px] font-extrabold">
                    {f.club_name}
                  </span>
                </span>
              </button>
            ))}
          </div>

          <div
            className="mt-3 flex items-center gap-2.5 border-t pt-2.5 flex-wrap"
            style={{ borderColor: 'var(--theme-mode-border)' }}
          >
            <span className="text-[12px] font-bold" style={{ color: 'var(--theme-mode-text-secondary)' }}>
              Favorite swims here — {favSwims.length} (by {favHere} swimmers)
            </span>
            <span className="flex-1" />
            <button
              type="button"
              onClick={() => onOpenSwimsScoped?.('favorites')}
              className="rounded-[9px] px-3 py-1.5 text-[12px] font-extrabold"
              style={{ background: 'var(--theme-personal-badge-bg)', color: 'var(--theme-personal-accent)' }}
            >
              Open in Swims →
            </button>
          </div>
        </>
      ) : (
        <span className="text-[12.5px] font-semibold" style={{ color: 'var(--theme-mode-text-muted)' }}>
          No favorites with results at this competition yet.
        </span>
      )}
    </section>
  );
}
