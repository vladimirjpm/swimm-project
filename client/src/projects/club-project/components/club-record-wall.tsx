import React, { useMemo, useState } from 'react';
import { useClubRecordWall, type ClubOfficialRecord } from '../../../hooks/useClubRecordWall';
import { ClubRecordCard, ClubRecordSection, ClubRecordTile, type PoolFilter } from './club-record-card';
import { compareDiscipline, groupByAge } from './age-sections';

/**
 * Record wall — ОФИЦИАЛЬНЫЕ рекорды, числящиеся за клубом: национальные, возрастные,
 * мастерс и мировые (таблица `Records`, импорт с isr.org.il и World Aquatics).
 *
 * Соседняя карточка «Season best» показывает совсем другое — лучшие времена клуба по нашим
 * протоколам; общая у них только форма (`club-record-card.tsx`). Здесь строка приходит из
 * внешнего справочника, поэтому:
 *  • ссылки на карточку пловца нет (у рекорда нет SwimmerId — только имя строкой);
 *  • возраст держателя = ступень рекорда (age_key), точного возраста в источнике нет;
 *  • эстафеты присутствуют (дистанция вида «4X50m», в имени четыре пловца через запятую).
 *
 * Сезона у карточки нет сознательно: рекорд действует, пока его не побили.
 */

interface Props {
  clubId: number;
}

/**
 * Подпись ступени: чем «выше» рекорд, тем ярче плашка (см. TONE ниже).
 * Возраста здесь нет — он вынесен в заголовок секции (карточка режется по ступеням).
 */
function scopeLabel(r: ClubOfficialRecord): string {
  if (r.region_type === 'world') return 'World record';
  const region = r.region_code ? r.region_code.toUpperCase() : 'National';
  return r.category === 'masters' ? `${region} masters` : `${region} record`;
}

const TONE: Record<string, string> = {
  world: 'var(--deep-gold)',
  open: 'var(--deep-gold)',
  age: 'var(--deep-accent)',
  masters: 'var(--deep-text-mute)',
};

function toneOf(r: ClubOfficialRecord): string {
  if (r.region_type === 'world') return TONE.world;
  return TONE[r.category] ?? 'var(--deep-text-mute)';
}

function ClubRecordWall({ clubId }: Props) {
  const [pool, setPool] = useState<PoolFilter>('all');
  const { data, loading } = useClubRecordWall(clubId, pool === 'all' ? null : pool);

  // Стена режется по ступеням (open → старшие → младшие → n/a), внутри секции —
  // по дисциплине. Источник отдаёт плоский список, поэтому группируем тут.
  const sections = useMemo(
    () =>
      groupByAge(data, (r) => ({ ageKey: r.age_key, category: r.category })).map((s) => ({
        ...s,
        items: [...s.items].sort((a, b) =>
          compareDiscipline(
            { style: a.style, distance: a.distance, poolType: a.pool_type },
            { style: b.style, distance: b.distance, poolType: b.pool_type },
          ),
        ),
      })),
    [data],
  );

  return (
    <ClubRecordCard
      title="Record wall"
      subtitle="National, age-group, masters and world records held by this club"
      count={data.length}
      countLabel="RECORDS"
      pool={pool}
      onPool={setPool}
      isEmpty={data.length === 0 && !loading}
      emptyText="No official records for this club"
      // Секции сами задают внутреннюю сетку, поэтому внешняя не нужна.
      plainBody
    >
      <div className="flex flex-col gap-4">
        {sections.map((section) => (
          <ClubRecordSection key={section.key} label={section.label} count={section.items.length}>
            {section.items.map((r, i) => (
              <ClubRecordTile
                // Ключ — по всей оси рекорда (регион × категория × ступень × дисциплина);
                // индекс в хвосте страхует от дублей источника.
                key={`${r.region_type}-${r.category}-${r.age_key}-${r.style}-${r.distance}-${r.pool_type}-${r.gender}-${i}`}
                gender={r.gender}
                topLine={scopeLabel(r)}
                topTone={toneOf(r)}
                // Style в Records — сырой ключ (individual_medley), это только косметика.
                secondLine={`${r.distance} ${r.style.replace(/_/g, ' ')} · ${r.pool_type.toUpperCase()}`}
                time={r.time}
                quality={r.issue_reason ? { kind: 'record', reason: r.issue_reason } : null}
                name={r.holder_name}
                footnote={r.record_date || '—'}
              />
            ))}
          </ClubRecordSection>
        ))}
      </div>
    </ClubRecordCard>
  );
}

export default ClubRecordWall;
