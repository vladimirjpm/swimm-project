import React from 'react';
import type { ClubKpi, ClubProfile } from '../../../hooks/useClubOverview';
import UI_ClubLogo from '../../components/mix/club-logo/club-logo';
import UI_FlagEmoji from '../../components/mix/flag-icon/flag-icon';
import DeepHeroBand from '../../components/deep/hero-band';
import { DeepBadge, DeepKpi } from '../../components/deep/kpi';
import { showcaseNoticeText } from '../../../utils/helpers/season-helper';

/**
 * Hero страницы клуба — ОДИН ИЗ вариантов шапки сущности (второй — группа). Корпус полосы и
 * кирпичи (бейдж, плитка KPI) общие: `deep/hero-band.tsx`, `deep/kpi.tsx`. Здесь остаётся
 * только то, что специфично клубу: логотип (или инициалы — это штатный вид, а не пустое
 * состояние), имя на иврите крупно + латиницей мелко, набор бейджей и состав KPI-ряда.
 *
 * Фото шапки есть (шаг A7): берётся из `hero_image_url`, а показывать ли блок — настройка
 * `show_hero_image` из таба Admin. Нет ссылки — рисуем ЗАГЛУШКУ, а не схлопываем колонку:
 * так правая колонка не прыгает между сущностями, и админу видно, куда класть картинку
 * (решение Влада 09.09.2026, docs/plans/entity-page-shell-plan.md §3.9). Прежний отказ от
 * фото (2026-08-01, «данных нет») этим отменён — данные появились.
 */

interface Props {
  club: ClubProfile;
  kpi: ClubKpi;
}

// Подписи скоупа у плиток свои (см. ниже), поэтому общий scopeLabel страницы шапке
// больше не нужен: у неё нет ни одной цифры, которая слушала бы карусель сезонов.
function ClubHero({ club, kpi }: Props) {
  return (
    <DeepHeroBand aside={club.show_hero_image ? <ClubPhoto url={club.hero_image_url} /> : undefined}>
      <div className="flex items-start gap-5">
        <UI_ClubLogo clubName={club.name} size={96} />

        <div className="min-w-0 flex-1">
          <h1
            className="truncate text-[40px] leading-tight"
            style={{ fontFamily: 'var(--deep-font-display)', color: 'var(--deep-text)' }}
          >
            {club.name}
          </h1>
          {club.name_en && (
            <div className="text-[13px] font-bold" style={{ color: 'var(--deep-text-mute)' }}>
              {club.name_en}
            </div>
          )}

          <div className="mt-3 flex flex-wrap items-center gap-2">
            {club.country_code && (
              <DeepBadge>
                <UI_FlagEmoji countryCode={club.country_code} /> {club.country_name ?? club.country_code}
              </DeepBadge>
            )}
            {club.official_group_slug && (
              <a href={`/groups/${club.official_group_slug}`} className="no-underline">
                <DeepBadge accent>Official group</DeepBadge>
              </a>
            )}
            {/* Бейджа «N swimmers» тут больше нет: пловцы стали плиткой KPI, а две
                одинаковые цифры в одной шапке читаются как ошибка. */}
            {club.first_season != null && <DeepBadge>since {club.first_season}</DeepBadge>}
          </div>
        </div>
      </div>

      {/* Плитки (решение Влада 2026-08-09). Скоуп у них РАЗНЫЙ и потому подписан у каждой:
          чемпионаты и победы — за всю историю клуба (карусель сезонов на них не влияет),
          рекорды — действующие, season bests — за витринный сезон, который режется
          последним зимним чемпионатом (docs/season-boundary-rule.md). */}
      <div className="mt-6 flex flex-wrap gap-8">
        <DeepKpi label="Championships" value={kpi.championships} hint="all time" />
        <DeepKpi
          label="Championship wins"
          value={kpi.championship_wins}
          hint="all time"
          gold={kpi.championship_wins > 0}
        />
        <DeepKpi label="Records" value={kpi.records} hint="in force" />
        <DeepKpi
          label="Season bests"
          value={kpi.season_bests}
          hint={kpi.showcase_season ? `season ${kpi.showcase_season}` : 'this season'}
          // Сентябрь-февраль: подпись говорит «season 2025/26», хотя идёт уже 2026/27.
          // Плитка узкая, полную оговорку в неё не вложить — отдаём её тултипом, тем же
          // текстом, что стоит плашкой над карточкой Season best (docs/season-boundary-rule.md).
          title={showcaseNoticeText(kpi.season_notice) ?? undefined}
        />
        <DeepKpi label="Swimmers" value={club.swimmer_count} hint="current roster" />
      </div>
    </DeepHeroBand>
  );
}

/** Фото клуба либо заглушка на его месте — колонка не схлопывается (план §3.9). */
function ClubPhoto({ url }: { url: string | null }) {
  if (url) {
    return (
      <img
        src={url}
        alt=""
        className="h-full min-h-[200px] w-full rounded-2xl border object-cover"
        style={{ borderColor: 'var(--deep-card-border)' }}
      />
    );
  }

  return (
    <div
      className="flex h-full min-h-[200px] w-full flex-col items-center justify-center gap-2 rounded-2xl border border-dashed"
      style={{ borderColor: 'var(--deep-card-border)', background: 'var(--deep-card-bg-row)' }}
      aria-hidden="true"
    >
      <span className="text-[28px]">🏊</span>
      <span className="text-[11.5px] font-extrabold" style={{ color: 'var(--deep-text-ghost)' }}>
        No club photo yet
      </span>
    </div>
  );
}

export default ClubHero;
