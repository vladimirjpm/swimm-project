import React, { useState } from 'react';
import type { ClubKpi, ClubProfile } from '../../../hooks/useClubOverview';
import { useFavoritesContext } from '../../../hooks/favorites-context';
import { useLoginModal } from '../../components/login-modal/login-modal-context';
import UI_ClubLogo from '../../components/mix/club-logo/club-logo';
import UI_FlagEmoji from '../../components/mix/flag-icon/flag-icon';
import DeepHeroBand from '../../components/deep/hero-band';
import DeepHeroPhoto from '../../components/deep/hero-photo';
import { DeepKpi, DeepKpiLink, DeepKpiRow } from '../../components/deep/kpi';
import { DeepCtaChip, DeepHeroSubline, DeepHeroTitle, DeepMenuChip } from '../../components/deep/hero-identity';
import { routes } from '../../../utils/routes';
import { showcaseNoticeText } from '../../../utils/helpers/season-helper';

/**
 * Hero страницы клуба — ОДИН ИЗ вариантов шапки сущности (второй — группа). Корпус полосы и
 * кирпичи общие: `deep/hero-band.tsx`, `deep/kpi.tsx`, `deep/hero-identity.tsx`. Раскладка —
 * хендофф group-club-changes, варианты 7a (телефон) и 6b (десктоп).
 *
 * Слева колонка логотипа, под логотипом — «✓ Following» (тап → меню с Unfollow; гостю
 * «+ Follow» → вход): колонка под логотипом всё равно пустая, и шапка не растёт. Справа имя
 * (иврит, до двух строк), подзаголовок `{name_en} · {флаг} {страна} · since {год}` и ссылка
 * «Official group →» — ТОЛЬКО если у клуба есть официальная группа: нет группы — нет ни
 * ссылки, ни заглушки, ни отступа.
 *
 * Страница клуба — только для чтения: ни тренеров, ни ролей, ни фиолетового. Всё, чем
 * можно управлять, живёт в официальной группе клуба.
 *
 * Фото шапки: `hero_image_url` (и `hero_image_mobile_url` для телефона), показывать ли —
 * настройка `show_hero_image` из таба Admin. Нет ссылки — ЗАГЛУШКА, а не схлопнутая колонка
 * (решение Влада 09.09.2026, подтверждено 28.09.2026).
 */

interface Props {
  club: ClubProfile;
  kpi: ClubKpi;
  /** Стартов в текущем скоупе — число плитки «Competitions →» (то же, что у таба History). */
  competitions: number;
  /** Подпись скоупа плитки соревнований: она слушает карусель сезонов. */
  scopeLabel: string;
  /** Переход в таб History. */
  onCompetitions: () => void;
}

function ClubHero({ club, kpi, competitions, scopeLabel, onCompetitions }: Props) {
  return (
    <DeepHeroBand
      aside={club.show_hero_image
        ? <DeepHeroPhoto url={club.hero_image_url} mobileUrl={club.hero_image_mobile_url} placeholder="No club photo yet" />
        : undefined}
    >
      <div className="flex items-start gap-3.5 min-[960px]:gap-5">
        <div className="flex w-[84px] flex-none flex-col items-center gap-2 min-[960px]:w-[112px]">
          {/* Логотип двумя размерами: у UI_ClubLogo размер числом, а не классом. */}
          <span className="min-[960px]:hidden"><UI_ClubLogo clubName={club.name} size={72} /></span>
          <span className="hidden min-[960px]:block"><UI_ClubLogo clubName={club.name} size={96} /></span>
          <FollowClubChip clubId={club.id} />
        </div>

        <div className="flex min-w-0 flex-1 flex-col gap-1 pt-1">
          <DeepHeroTitle size="club">{club.name}</DeepHeroTitle>
          <DeepHeroSubline
            parts={[
              club.name_en && club.name_en !== club.name ? club.name_en : null,
              club.country_code ? (
                <span className="whitespace-nowrap [&_img]:inline-block [&_img]:h-[12px] [&_img]:w-4 [&_img]:align-[-1px]"><UI_FlagEmoji countryCode={club.country_code} /> {club.country_name ?? club.country_code}</span>
              ) : null,
              club.first_season != null ? `since ${club.first_season}` : null,
            ]}
          />
          {club.official_group_slug && (
            <a
              href={routes.group(club.official_group_slug)}
              title={club.official_group_name ?? undefined}
              className="mt-1 flex items-center gap-1.5 self-start text-[12px] font-extrabold no-underline min-[960px]:text-[13px]"
              style={{ color: 'var(--deep-accent)' }}
            >
              <span
                aria-hidden="true"
                className="flex h-[18px] w-[18px] items-center justify-center rounded-[5px] text-[9px] min-[960px]:h-5 min-[960px]:w-5 min-[960px]:text-[10px]"
                style={{
                  background: 'var(--deep-accent-grad)',
                  color: 'var(--deep-accent-ink)',
                  fontFamily: 'var(--deep-font-display)',
                }}
              >
                {(club.official_group_name?.trim()[0] ?? 'G').toUpperCase()}
              </span>
              Official group →
            </a>
          )}
        </div>
      </div>

      {/* Плитки (решение Влада 2026-08-09). Скоуп у них РАЗНЫЙ и потому подписан у каждой:
          чемпионаты и победы — за всю историю клуба (карусель сезонов на них не влияет),
          рекорды — действующие, season bests — за витринный сезон, который режется
          последним зимним чемпионатом (docs/season-boundary-rule.md). Нулевые не кладём.
          Телефон — сетка 3×2 с короткими подписями. */}
      <DeepKpiRow>
        {kpi.championships > 0 && (
          <DeepKpi label="Championships" shortLabel="Champs" value={kpi.championships} hint="all time" />
        )}
        {kpi.championship_wins > 0 && (
          <DeepKpi
            label="Championship wins"
            shortLabel="Wins"
            value={kpi.championship_wins}
            hint="all time"
            gold
          />
        )}
        {kpi.records > 0 && <DeepKpi label="Records" value={kpi.records} hint="in force" />}
        {kpi.season_bests > 0 && (
          <DeepKpi
            label="Season bests"
            shortLabel={kpi.showcase_season ? `SB ${kpi.showcase_season.replace(/^20/, '')}` : 'Season bests'}
            value={kpi.season_bests}
            hint={kpi.showcase_season ? `season ${kpi.showcase_season}` : 'this season'}
            // Сентябрь-февраль: подпись говорит «season 2025/26», хотя идёт уже 2026/27.
            // Плитка узкая, полную оговорку в неё не вложить — отдаём её тултипом, тем же
            // текстом, что стоит плашкой над карточкой Season best (docs/season-boundary-rule.md).
            title={showcaseNoticeText(kpi.season_notice) ?? undefined}
          />
        )}
        {club.swimmer_count > 0 && (
          <DeepKpi label="Swimmers" value={club.swimmer_count} hint="current roster" />
        )}
        {competitions > 0 && (
          <DeepKpiLink label="Competitions →" value={competitions} hint={scopeLabel} onClick={onCompetitions} />
        )}
      </DeepKpiRow>
    </DeepHeroBand>
  );
}

/**
 * «✓ Following» под логотипом — клуб в избранном (П1 плана docs/plans/hubgroup-club-subscription-plan.md).
 * Отписка — в меню чипа, а не второй кнопкой.
 *
 * Избранный клуб в пловцов НЕ разворачивается (решение Влада 10.09.2026): это сигнал «мы» —
 * голубой клуб рядом с золотым «моим» пловцом, — а не 160 сердечек. Поэтому у клубов свой
 * лимит (3 по умолчанию), и лимит пловцов кнопка не трогает.
 */
function FollowClubChip({ clubId }: { clubId: number }) {
  const { isAuthenticated, loading, favoriteClubIds, toggleFavoriteClub, fullHint } = useFavoritesContext();
  const { openLoginModal } = useLoginModal();
  const [busy, setBusy] = useState(false);

  // Пока избранное не приехало, состояние неизвестно — держим место, чтобы шапка не прыгнула,
  // и не мигаем «Follow».
  if (loading) return <span aria-hidden="true" className="h-[26px] min-[960px]:h-[30px]" />;

  // Гостю — та же кнопка, клик ведёт во вход: фича видна, но требует логина.
  if (!isAuthenticated) {
    return (
      <DeepCtaChip size="follow" filled onClick={openLoginModal} title="Sign in to follow this club">
        + Follow
      </DeepCtaChip>
    );
  }

  const toggle = async () => {
    setBusy(true);
    try { await toggleFavoriteClub(clubId); } finally { setBusy(false); }
  };

  if (favoriteClubIds.has(clubId)) {
    return (
      <DeepMenuChip
        variant="follow"
        label="✓ Following"
        busy={busy}
        title="The club is in your favorites"
        items={[{ label: 'Unfollow', danger: true, onSelect: toggle }]}
      />
    );
  }

  // Лимит избранных клубов исчерпан — кнопка видна, но не жмётся; причина — в title
  // (колонка под логотипом узкая, подпись под ней не поместится).
  const blockedHint = fullHint('club');
  if (blockedHint) {
    return (
      <button
        type="button"
        aria-disabled="true"
        title={blockedHint}
        className="hp-mono flex h-[26px] w-full cursor-not-allowed items-center justify-center rounded-[8px] border px-1 text-[10.5px] font-extrabold opacity-50 min-[960px]:h-[30px] min-[960px]:rounded-[9px] min-[960px]:text-[12px]"
        style={{ background: 'var(--deep-accent-chip)', borderColor: 'var(--deep-accent-border)', color: 'var(--deep-accent)' }}
      >
        + Follow
      </button>
    );
  }

  return (
    <DeepCtaChip size="follow" filled busy={busy} onClick={toggle} title="Add the club to your favorites">
      + Follow
    </DeepCtaChip>
  );
}

export default ClubHero;
