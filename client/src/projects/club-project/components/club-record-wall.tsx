import React, { useMemo, useState } from 'react';
import { useClubRecordWall, type ClubOfficialRecord } from '../../../hooks/useClubRecordWall';
import { ClubRecordCard, ClubRecordSection, ClubRecordTile, type PoolFilter } from './club-record-card';
import { compareDiscipline, groupByAge } from './age-sections';
import UI_RecordBadge, { type RecordKind } from '../../components/mix/record-badge/record-badge';

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
 * Класс рекорда для общего `UI_RecordBadge`. Мировой рекорд живёт в `region_type`, а не в
 * `category`, — поэтому он проверяется первым.
 */
function recordKindOf(r: ClubOfficialRecord): RecordKind {
  if (r.region_type === 'world') return 'world';
  if (r.category === 'masters') return 'masters';
  return r.category === 'open' ? 'national' : 'age';
}

/**
 * Подпись рядом с бейджем: откуда рекорд. Класс («какого он веса») несёт бейдж, здесь —
 * только область, иначе строка повторяла бы одно и то же дважды.
 * Возраста нет: он в заголовке секции — карточка режется по ступеням.
 */
function scopeLabel(r: ClubOfficialRecord): string {
  if (r.region_type === 'world') return 'World';
  return r.region_code ? r.region_code.toUpperCase() : 'National';
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
                // Класс рекорда — тем же бейджем, что в H2H, таблице результатов и на
                // стене пловца. Цветную подпись он заменил целиком: цвет ступени жил
                // ТОЛЬКО здесь и спорил с продуктовым правилом (cyan = «быстрее», а не
                // «возрастной рекорд»).
                topLine={(
                  <span className="flex items-center gap-1.5">
                    <UI_RecordBadge
                      kind={recordKindOf(r)}
                      scope={r.age_key ? `${r.category} ${r.age_key}` : r.category}
                    />
                    <span className="truncate">{scopeLabel(r)}</span>
                  </span>
                )}
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
