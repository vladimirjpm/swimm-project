import React, { useEffect, useMemo, useState } from 'react';
import { useAppSelector } from '../../../../store/store';
import { useFavoritesContext } from '../../../../hooks/favorites-context';
import type { AllUserMediaDto } from '../../../my-media-project/use-all-my-media';
import { HelperMedia } from '../../../../utils/helpers';
import HelperSwimmer from '../../../../utils/helpers/helper-swimmer';
import type { CompetitionOverview } from './types';

// Персональная полоса шапки соревнования (design_handoff_competition_overview):
// ⭐ primary favorite (мои заплывы здесь) · ❤️ Favorites · My media. ТОЛЬКО залогиненному —
// гостю модуль не рендерится вовсе. Красится ИСКЛЮЧИТЕЛЬНО токенами --theme-personal-*
// (настройка per-theme без правки компонентов). Live-контента нет (никаких «next: …»).

interface Props {
  overview: CompetitionOverview | null;
  /** Переход в Swims с предустановленным скоупом (?filter=my|favorites). */
  onOpenSwims(scope: 'my' | 'favorites'): void;
  onOpenMedia(): void;
  onAddMedia?: () => void;
}

const cardStyle: React.CSSProperties = {
  background: 'var(--theme-personal-bg)',
  border: '1px solid var(--theme-personal-border)',
  color: 'var(--theme-mode-text)',
};

function Badge({ children }: { children: React.ReactNode }) {
  return (
    <span
      className="rounded-full px-2 py-0.5 text-[10px] font-extrabold uppercase tracking-wide"
      style={{ background: 'var(--theme-personal-badge-bg)', color: 'var(--theme-personal-accent)' }}
    >
      {children}
    </span>
  );
}

export default function CompetitionPersonalStrip({ overview, onOpenSwims, onOpenMedia, onAddMedia }: Props) {
  const { isAuthenticated, favorites, primarySwimmerId, favoriteSwimmerIds } = useFavoritesContext();
  const selectedSource = useAppSelector((s) => s.dataSourceSelected);

  // Мои медиа на этом соревновании — ленивый одноразовый fetch (полоса только залогиненному).
  const [myMedia, setMyMedia] = useState<AllUserMediaDto[]>([]);
  const dayIds = useMemo(
    () => new Set((overview?.days ?? []).map((d) => d.competition_id)),
    [overview],
  );
  useEffect(() => {
    if (!isAuthenticated || dayIds.size === 0) { setMyMedia([]); return; }
    let cancelled = false;
    (async () => {
      try {
        const r = await fetch('/api/me/media', { credentials: 'include' });
        if (!r.ok || cancelled) return;
        const items: AllUserMediaDto[] = await r.json();
        setMyMedia(items.filter((m) => m.competition_id != null && dayIds.has(m.competition_id)));
      } catch { /* полоса живёт без медиа-счётчика */ }
    })();
    return () => { cancelled = true; };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [isAuthenticated, dayIds]);

  if (!isAuthenticated) return null;

  const results = selectedSource?.results ?? [];
  // Матчинг — только через HelperSwimmer.resultBelongsToSwimmer (эстафеты по составу ног)
  const matches = (r: any, id: number) => HelperSwimmer.resultBelongsToSwimmer(r, id);
  const mySwims = primarySwimmerId != null
    ? results.filter((r: any) => matches(r, primarySwimmerId))
    : [];
  const favIds = [...favoriteSwimmerIds].filter((id) => id !== primarySwimmerId);
  const favSwims = results.filter((r: any) => favIds.some((id) => matches(r, id)));
  const favHere = favIds.filter((id) => results.some((r: any) => matches(r, id))).length;

  const primaryName =
    favorites.find((f) => f.swimmer_id === primarySwimmerId)?.swimmer_name ?? 'Me';
  const myBest = mySwims.reduce(
    (best: any, r: any) => (r.international_points > (best?.international_points ?? 0) ? r : best),
    null,
  );
  const myMedals = {
    gold: mySwims.filter((r: any) => Number(r.position) === 1).length,
    silver: mySwims.filter((r: any) => Number(r.position) === 2).length,
    bronze: mySwims.filter((r: any) => Number(r.position) === 3).length,
  };

  // Совсем нечего показать (нет primary, избранных и медиа) — полоса схлопывается.
  if (primarySwimmerId == null && favHere === 0 && myMedia.length === 0) return null;

  return (
    <div
      className="flex gap-2 overflow-x-auto px-3 py-2.5 lg:grid lg:grid-cols-[1fr_1fr_1.4fr]"
      style={{ background: 'var(--theme-mode-surface)' }}
    >
      {/* ⭐ Primary favorite */}
      {primarySwimmerId != null && (
        <button
          type="button"
          onClick={() => onOpenSwims('my')}
          className="flex min-w-[230px] flex-1 cursor-pointer flex-col items-start gap-1.5 rounded-[10px] p-2.5 text-left lg:min-w-0"
          style={cardStyle}
        >
          <span className="flex items-center gap-1.5">
            <Badge>⭐ {primaryName}</Badge>
          </span>
          <span className="text-[12px] font-bold" dir="auto">
            {myBest ? `${myBest.international_points} pts` : null}
            {(myMedals.gold || myMedals.silver || myMedals.bronze) ? (
              <span className="ml-1.5">
                {myMedals.gold > 0 && `🥇 ${myMedals.gold} `}
                {myMedals.silver > 0 && `🥈 ${myMedals.silver} `}
                {myMedals.bronze > 0 && `🥉 ${myMedals.bronze}`}
              </span>
            ) : null}
            {!myBest && !mySwims.length && (
              <span style={{ color: 'var(--theme-mode-text-muted)' }}>No swims here yet</span>
            )}
          </span>
          <span className="text-[12px] font-extrabold" style={{ color: 'var(--theme-personal-accent)' }}>
            My swims here — {mySwims.length} →
          </span>
        </button>
      )}

      {/* ❤️ Favorites */}
      {favHere > 0 && (
        <button
          type="button"
          onClick={() => onOpenSwims('favorites')}
          className="flex min-w-[230px] flex-1 cursor-pointer flex-col items-start gap-1.5 rounded-[10px] p-2.5 text-left lg:min-w-0"
          style={cardStyle}
        >
          <Badge>❤️ Favorites</Badge>
          <span className="text-[12px] font-bold" style={{ color: 'var(--theme-mode-text-secondary)' }}>
            {favSwims.length} swims by {favHere} swimmers
          </span>
          <span className="text-[12px] font-extrabold" style={{ color: 'var(--theme-personal-accent)' }}>
            My favorites here — {favHere} →
          </span>
        </button>
      )}

      {/* My media */}
      <div
        className="flex min-w-[230px] flex-[1.4] flex-col gap-1.5 rounded-[10px] p-2.5 lg:min-w-0"
        style={cardStyle}
      >
        <span className="flex items-center justify-between gap-2">
          <Badge>My media — {myMedia.length}</Badge>
          {onAddMedia && (
            <button
              type="button"
              onClick={onAddMedia}
              className="cursor-pointer rounded-[8px] px-2 py-1 text-[11px] font-extrabold"
              style={{
                background: 'var(--theme-personal-badge-bg)',
                color: 'var(--theme-personal-accent)',
                border: '1px solid var(--theme-personal-border)',
              }}
            >
              ＋ Add video / photo
            </button>
          )}
        </span>
        {myMedia.length > 0 ? (
          <button
            type="button"
            onClick={onOpenMedia}
            className="flex cursor-pointer gap-1.5 bg-transparent p-0"
            title="Open Media tab"
          >
            {myMedia.slice(0, 4).map((m) => {
              const thumb = HelperMedia.resolveThumbUrl(m.media_type, m.source_type, m.url);
              return (
                <span
                  key={m.id}
                  className="relative block h-[38px] w-[64px] overflow-hidden rounded-[7px]"
                  style={{ background: 'color-mix(in srgb, var(--theme-mode-text) 10%, transparent)' }}
                >
                  {thumb && <img src={thumb} alt="" loading="lazy" className="h-full w-full object-cover" />}
                  {m.media_type === 'video' && (
                    <span className="absolute inset-0 flex items-center justify-center text-[13px] text-white drop-shadow">▶</span>
                  )}
                </span>
              );
            })}
          </button>
        ) : (
          <span className="text-[12px] font-semibold" style={{ color: 'var(--theme-mode-text-muted)' }}>
            No media from you here yet
          </span>
        )}
      </div>
    </div>
  );
}
