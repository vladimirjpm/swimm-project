import React from 'react';
import DeepHeroBand from '../../components/deep/hero-band';
import DeepHeroPhoto from '../../components/deep/hero-photo';
import { DeepKpi, DeepKpiLink, DeepKpiRow } from '../../components/deep/kpi';
import { DeepHeroSubline, DeepHeroTitle } from '../../components/deep/hero-identity';
import UI_FlagEmoji from '../../components/mix/flag-icon/flag-icon';
import { routes } from '../../../utils/routes';
import { GroupIcon, LinkChips } from './group-bits';
import GroupMembershipChip, { type GroupMembershipState } from './group-membership';
import GroupTrainingBlock from './group-training-slots';
import type { TrainingRsvpState } from '../use-training-rsvp';
import type { HubGroupDetails } from '../types';

/**
 * Шапка группы — вариант шапки сущности (первый — клуб, `club-hero.tsx`). Раскладка —
 * хендофф group-club-changes: телефон 2a, десктоп 4a (+5c).
 *
 * Сверху вниз: фото → строка «кто это» (аватар, имя, подзаголовок, чип членства) → блок
 * тренировок → KPI. Справа от имени НИЧЕГО нет: кнопки «Competitions →» и «Leave group»
 * съедали ~330px строки имени; первая стала плиткой KPI, вторая — пунктом меню «✓ Member ⋯».
 *
 * Фото приходит уже разрешённым (`hero_image_url`, для телефона — `hero_image_mobile_url`):
 * указатель «взять из медиа» решает сервер. Ссылки нет — ЗАГЛУШКА, а не схлопнутая колонка
 * (решение Влада 09.09.2026, подтверждено 28.09.2026). Выключает блок `show_hero_image`.
 *
 * KPI — только из того, что уже пришло в ответе; нулевые не показываем (хендофф: «Gold 0»
 * читался упрёком, поэтому плитки золота больше нет вовсе — медали видны в Season).
 */

interface Props {
  group: HubGroupDetails;
  membership: GroupMembershipState;
  /** Участник или управляющий — видит строку NEXT, а не голое расписание. */
  insider: boolean;
  /** Ответы на ближайшее занятие (Ш2) — один экземпляр на страницу. */
  rsvp?: TrainingRsvpState | null;
  /** «Who's coming →» — переход в таб Trainings. */
  onWhosComing?: () => void;
  /** Над шапкой стоит баннер «Are you coming?» (Ш4) — блок NEXT на это время прячется. */
  hideTraining?: boolean;
}

function GroupHero({ group, membership, insider, rsvp, onWhosComing, hideTraining = false }: Props) {
  const real = !group.is_virtual && group.id > 0;
  const competitions = group.competitions?.length ?? 0;

  return (
    <DeepHeroBand
      aside={group.show_hero_image === false
        ? undefined
        : (
          <DeepHeroPhoto
            url={group.hero_image_url}
            mobileUrl={group.hero_image_mobile_url}
            placeholder="No group photo yet"
          />
        )}
    >
      <div className="flex h-full flex-col gap-3 min-[960px]:gap-[18px]">
        <div className="flex items-start gap-3.5 min-[960px]:gap-[18px]">
          <GroupIcon iconUrl={group.icon_url} name={group.name_en || group.name} size="hero" />

          <div className="min-w-0 flex-1">
            <DeepHeroTitle size="group">{group.name}</DeepHeroTitle>

            {/* Телефон: подзаголовок, под ним чип; десктоп — одной строкой (4a). */}
            <div className="mt-0.5 flex flex-col items-start gap-2 min-[960px]:mt-2 min-[960px]:flex-row min-[960px]:flex-wrap min-[960px]:items-center min-[960px]:gap-3">
              <DeepHeroSubline
                parts={[
                  group.name_en && group.name_en !== group.name ? group.name_en : null,
                  // Флаг и город — одним неразрывным куском: иначе флаг оставался строкой выше.
                  group.country || group.location ? (
                    <span className="whitespace-nowrap [&_img]:inline-block [&_img]:h-[12px] [&_img]:w-4 [&_img]:align-[-1px]">
                      {group.country ? <UI_FlagEmoji countryCode={group.country} /> : '📍'}{' '}
                      {group.location && <bdi>{group.location}</bdi>}
                    </span>
                  ) : null,
                  clubRelation(group),
                ]}
              />
              <GroupMembershipChip group={group} membership={membership} />
            </div>
          </div>
        </div>

        {/* Описание и ссылки — только от 960px: на телефоне шапка и так длинная (хендофф
            мерил ~780px до табов), а ссылки группы дублирует таб Overview. */}
        {(group.description || group.links.length > 0) && (
          <div className="hidden min-[960px]:block">
            {group.description && (
              <p className="m-0 max-w-[640px] text-[13.5px] leading-[1.55]" style={{ color: 'var(--deep-text-mute)' }}>
                {group.description}
              </p>
            )}
            {group.links.length > 0 && <div className="mt-3"><LinkChips links={group.links} /></div>}
          </div>
        )}

        {!hideTraining && (
          <GroupTrainingBlock
            schedule={group.training_schedule}
            next={group.next_training}
            mode={insider ? 'member' : 'guest'}
            rsvp={rsvp}
            onWhosComing={onWhosComing}
          />
        )}

        <div className="min-[960px]:mt-auto">
          <DeepKpiRow>
            {group.members.length > 0 && (
              <DeepKpi label="Following" value={group.members.length} hint="swimmers" />
            )}
            {group.bests.length > 0 && (
              <DeepKpi label="Records" value={group.bests.length} hint="best in the group" />
            )}
            {/* Соревнования — ОТДЕЛЬНЫЙ экран (`/groups/{slug}/results`), а не срез этого;
                раньше туда вела кнопка у имени (план §5.1), теперь — плитка с числом. */}
            {real && competitions > 0 && (
              <DeepKpiLink
                label="Competitions →"
                value={competitions}
                hint="all meets"
                href={routes.groupResults(group.slug)}
              />
            )}
          </DeepKpiRow>
        </div>
      </div>
    </DeepHeroBand>
  );
}

/**
 * Связь с клубом — частью подзаголовка, а не бейджами отдельной строкой (их было до
 * трёх, и на телефоне они занимали собственный ряд). Где есть куда вести — ссылкой.
 * Функция, а не компонент: подзаголовку нужно знать, есть ли часть, чтобы не ставить «·».
 */
function clubRelation(group: HubGroupDetails): React.ReactNode {
  const link = 'no-underline font-extrabold';
  const accent = { color: 'var(--deep-accent)' };

  // Копию клуба открыли по ссылке мимо каталога — показываем, где «лицо клуба» (П4).
  if (group.official_group_slug) {
    return (
      <a href={routes.group(group.official_group_slug)} className={link} style={accent}>
        Official group: <bdi>{group.official_group_name}</bdi> →
      </a>
    );
  }
  if (group.is_official && group.club_name) {
    return <span style={accent}>Official group of <bdi>{group.club_name}</bdi></span>;
  }
  // Состав из клуба (подписка).
  if (group.followed_club_name) {
    return group.followed_club_id ? (
      <a href={routes.club(group.followed_club_id)} className={link} style={accent}>
        Follows <bdi>{group.followed_club_name}</bdi>
      </a>
    ) : <span>Follows <bdi>{group.followed_club_name}</bdi></span>;
  }
  if (group.club_name) return <span>Club: <bdi>{group.club_name}</bdi></span>;
  return null;
}

export default GroupHero;
