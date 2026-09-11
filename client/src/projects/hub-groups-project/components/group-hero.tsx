import React from 'react';
import DeepHeroBand from '../../components/deep/hero-band';
import { DeepBadge, DeepKpi } from '../../components/deep/kpi';
import UI_ClubIcon from '../../components/mix/club-icon/club-icon';
import UI_FlagEmoji from '../../components/mix/flag-icon/flag-icon';
import { routes } from '../../../utils/routes';
import { GroupIcon, JoinButton, LinkChips } from './group-bits';
import GroupTrainingSlots from './group-training-slots';
import type { HubGroupDetails } from '../types';

/**
 * Шапка группы — ВТОРОЙ вариант шапки сущности (первый — клуб, `club-hero.tsx`).
 *
 * Корпус полосы и кирпичи общие (`deep/hero-band.tsx`, `deep/kpi.tsx`), своё здесь — состав:
 * аватар группы, имя, строка меты, чипы ссылок, кнопки действий и фото справа.
 *
 * Фото приходит уже разрешённым (`hero_image_url`): сервер сам решает, взять его из медиа
 * по указателю `hero.mediaId` или из колонки-обложки. Ссылки нет — рисуем ЗАГЛУШКУ, а не
 * схлопываем колонку (решение Влада 09.09.2026, план §3.9): так правая колонка не прыгает
 * между сущностями, и админу видно, куда класть картинку. Выключает блок настройка
 * `show_hero_image` из таба Admin.
 *
 * KPI считаются из того, что уже пришло в ответе: участники, рекорды группы, золото сезона.
 * Ни одной цифры, которой нет в данных, тут не выдумывается.
 */

interface Props {
  group: HubGroupDetails;
}

function GroupHero({ group }: Props) {
  const golds = group.standings.reduce((sum, s) => sum + s.golds, 0);

  return (
    <DeepHeroBand aside={group.show_hero_image === false ? undefined : <GroupPhoto group={group} />}>
      <div className="flex flex-wrap items-start gap-5">
        <GroupIcon iconUrl={group.icon_url} name={group.name_en || group.name} size="lg" />

        <div className="min-w-0 flex-1">
          <h1
            className="truncate text-[34px] leading-tight"
            style={{ fontFamily: 'var(--deep-font-display)', color: 'var(--deep-text)' }}
          >
            {group.name}
          </h1>
          {group.name_en && group.name_en !== group.name && (
            <div className="text-[13px] font-bold" style={{ color: 'var(--deep-text-mute)' }}>
              {group.name_en}
            </div>
          )}

          <div className="mt-3 flex flex-wrap items-center gap-2">
            {(group.country || group.location) && (
              <DeepBadge>
                {group.country ? <UI_FlagEmoji countryCode={group.country} /> : '📍'} {group.location}
              </DeepBadge>
            )}
            {group.is_official && group.club_name && (
              <DeepBadge accent>
                <UI_ClubIcon clubName={group.club_name} iconWidth="6" styleType="icon-notext" />{' '}
                Official group of {group.club_name}
              </DeepBadge>
            )}
            {!group.is_official && group.club_name && <DeepBadge>Club: {group.club_name}</DeepBadge>}
            {/* Состав из клуба (подписка). У официальной своего бейджа хватает — «Official group of». */}
            {!group.is_official && group.followed_club_name && (
              group.followed_club_id ? (
                <a href={routes.club(group.followed_club_id)} className="no-underline">
                  <DeepBadge>Follows <bdi>{group.followed_club_name}</bdi></DeepBadge>
                </a>
              ) : (
                <DeepBadge>Follows <bdi>{group.followed_club_name}</bdi></DeepBadge>
              )
            )}
            {/* Копию клуба открыли по ссылке мимо каталога — показываем, где «лицо клуба» (П4). */}
            {group.official_group_slug && (
              <a href={routes.group(group.official_group_slug)} className="no-underline">
                <DeepBadge accent>Official group: <bdi>{group.official_group_name}</bdi> →</DeepBadge>
              </a>
            )}
          </div>

          {group.description && (
            <p
              className="mt-3 max-w-[640px] text-[13.5px] leading-[1.55]"
              style={{ color: 'var(--deep-text-mute)' }}
            >
              {group.description}
            </p>
          )}

          {group.links.length > 0 && <div className="mt-3"><LinkChips links={group.links} /></div>}
        </div>

        <div className="flex shrink-0 flex-wrap items-center gap-2">
          {/* Соревнования — ОТДЕЛЬНЫЙ экран (`/groups/{slug}/results`), а не срез этого,
              поэтому кнопка шапки, а не таб (план §5.1). */}
          {!group.is_virtual && group.id > 0 && (
            <a
              href={routes.groupResults(group.slug)}
              className="hp-mono shrink-0 rounded-[10px] border px-4 py-2 text-[13px] font-extrabold no-underline"
              style={{
                borderColor: 'var(--deep-accent-border)',
                background: 'var(--deep-accent-chip)',
                color: 'var(--deep-accent)',
              }}
            >
              Competitions →
            </a>
          )}
          <JoinButton group={group} />
        </div>
      </div>

      <GroupTrainingSlots schedule={group.training_schedule} next={group.next_training} />

      <div className="mt-6 flex flex-wrap gap-8">
        <DeepKpi label="Swimmers" value={group.members.length} hint="in the roster" />
        <DeepKpi label="Records" value={group.bests.length} hint="best in the group" />
        <DeepKpi
          label="Gold"
          value={golds}
          hint={group.season_label ? `season ${group.season_label}` : 'this season'}
          gold={golds > 0}
        />
      </div>
    </DeepHeroBand>
  );
}

/** Фото группы либо заглушка на её месте — колонка не схлопывается (план §3.9). */
function GroupPhoto({ group }: Props) {
  // Указатель «взять из медиа» разрешает сервер — здесь одно готовое поле.
  if (group.hero_image_url) {
    return (
      <img
        src={group.hero_image_url}
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
        No group photo yet
      </span>
    </div>
  );
}

export default GroupHero;
