import React, { useMemo, useState } from 'react';
import { useClubSeasonBest, type ClubSeasonBestItem } from '../../../hooks/useClubSeasonBest';
import { routes } from '../../../utils/routes';
import { ClubRecordCard, ClubRecordSection, ClubRecordTile, type PoolFilter } from './club-record-card';
import { compareDiscipline, groupByAge } from './age-sections';
import UI_SeasonNotice from '../../components/mix/season-notice/season-notice';

/**
 * Season best — заплывы пловцов клуба, которые в этом сезоне ЛУЧШИЕ ПО СТРАНЕ в своём слоте
 * (дисциплина × бассейн × пол × возрастная ступень). Лидер считается по всей базе, а не
 * внутри клуба: карточка отвечает «наши первые в Израиле», а не «наше лучшее».
 *
 * Ступени — как в таблицах федерации: до 18 лет каждый год отдельно, «adults» 19–24 одной
 * ступенью, мастерс пятилетками. Без ступеней слот забирал бы самый быстрый пловец страны
 * любого возраста, и дети не попадали бы в карточку никогда.
 *
 * ⚠ Два обязательства перед читателем, которые нельзя потерять:
 *  • карточка про ОДИН сезон и глобальный фильтр сезона страницы не слушает
 *    (решение Влада 2026-08-01);
 *  • «первый в Израиле» = первый среди ИМПОРТИРОВАННОГО, поэтому под заголовком стоит
 *    число стартов, вошедших в расчёт. Не убирать: юниорских и взрослых чемпионатов в
 *    базе может не быть вовсе, и лидерство по этим ступеням будет считаться по остаткам.
 *
 * Официальные рекорды (нац./возрастные/мастерс/мировые) — соседняя карточка
 * `club-record-wall.tsx`; общая у них форма (`club-record-card.tsx`), но не содержимое.
 */

interface Props {
  clubId: number;
}

/** Подпись дисциплины на плитке: «50m breaststroke · 25M». */
function tileDiscipline(it: ClubSeasonBestItem): string {
  // Style.Name из БД сырой (individual_medley) — только косметика показа.
  const style = it.style_name.replace(/_/g, ' ');
  const pool = it.pool_type ? ` · ${it.pool_type.toUpperCase()}` : '';
  return `${it.distance}m ${style}${pool}`;
}

function ClubRecords({ clubId }: Props) {
  const [pool, setPool] = useState<PoolFilter>('all');
  const { groups, seasonLabel, total, meets, notice, loading } = useClubSeasonBest(
    clubId,
    pool === 'all' ? null : pool,
  );

  // Карточка режется по возрасту, а не по дисциплине (как Record wall): сервер отдаёт
  // группы по дисциплинам, поэтому здесь их разворачиваем и пересобираем по ступеням.
  const sections = useMemo(
    () =>
      groupByAge(
        groups.flatMap((g) => g.items),
        (it) => ({ ageKey: it.age_key }),
      ).map((s) => ({
        ...s,
        items: [...s.items].sort((a, b) =>
          compareDiscipline(
            { style: a.style_name, distance: `${a.distance}m`, poolType: a.pool_type },
            { style: b.style_name, distance: `${b.distance}m`, poolType: b.pool_type },
          ),
        ),
      })),
    [groups],
  );

  return (
    <ClubRecordCard
      title={seasonLabel ? `Season ${seasonLabel} best` : 'Season best'}
      // «this season» тут врало бы с сентября по февраль: показан витринный сезон, а идёт
      // уже следующий. Называем сезон по имени, пока оно известно.
      subtitle={
        seasonLabel
          ? `Club swimmers ranked #1 in Israel in ${seasonLabel} · among ${meets} meets in our database`
          : `Club swimmers ranked #1 in Israel this season · among ${meets} meets in our database`
      }
      count={total}
      countLabel="SB in IL"
      pool={pool}
      onPool={setPool}
      isEmpty={groups.length === 0 && !loading}
      // Сезон называем по имени: с сентября по февраль «this season» врало бы — показан
      // прошлый сезон, а не идущий (docs/season-boundary-rule.md).
      emptyText={
        seasonLabel
          ? `No national-best times in ${seasonLabel}`
          : 'No national-best times this season'
      }
      // Карточка всегда стоит на витринном сезоне (сезон она не выбирает), поэтому season
      // не передаём — заметка сама скажет, что показан прошлый.
      notice={<UI_SeasonNotice notice={notice} />}
      // Секции сами задают внутреннюю сетку, поэтому внешняя не нужна.
      plainBody
    >
      <div className="flex flex-col gap-4">
        {sections.map((section) => (
          <ClubRecordSection key={section.key} label={section.label} count={section.items.length}>
            {section.items.map((it) => (
              <ClubRecordTile
                key={`${it.style_name}-${it.distance}-${it.pool_type}-${it.gender}-${it.age_key}`}
                gender={it.gender}
                // Ступень «первый в Израиле» стоит там же, где в Record wall ступень рекорда.
                topLine="SB IL"
                topTone="var(--deep-gold)"
                secondLine={tileDiscipline(it)}
                time={it.time_original}
                quality={it.suspect_reason ? { kind: 'protocol', reason: it.suspect_reason } : null}
                name={it.swimmer_name}
                footnote={it.date}
                href={routes.swimmer(it.swimmer_id)}
              />
            ))}
          </ClubRecordSection>
        ))}
      </div>
    </ClubRecordCard>
  );
}

export default ClubRecords;
