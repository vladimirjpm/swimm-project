import React from 'react';
import UI_SwimmStyleIcon from '../../../components/mix/swimm-style-icon/swimm-style-icon';
import UI_SwimTime from '../../../components/mix/swim-time/swim-time';
import { formatApproxTime } from './start-list-helpers';
import { bandLabel } from './plan-model';
import type { PlanRow } from './plan-model';

/**
 * Строка заплыва в карточке плана — формат **D3** (шаг Т7, хендофф §1.3).
 *
 * Сетка `74px | 1fr`: слева плитка времени во всю высоту, справа — дисциплина и имена.
 * Главное число строки — ВРЕМЯ СТАРТА, поэтому оно и стоит отдельной плиткой; номер
 * заплыва в неё не идёт (он в метке справа), иначе плитка читается как два числа сразу.
 *
 * Почему не общий `SwimRow`: он собран под результат (место, медаль, очки, дуга уровня) и
 * в стартовом протоколе показывал бы то, чего ещё не произошло — правило №2 исходного
 * хендоффа, откат 28.08.2026. Общими остаются части: `UI_SwimmStyleIcon`, `UI_SwimTime`.
 *
 * Несколько выбранных в одном заплыве — ОДНА строка: время и дисциплина общие, имена
 * столбиком, у каждого своя дорожка (склейка в `groupPlanRows`).
 */
export default function SwimRowD3({ row, showNames, onClick }: {
  row: PlanRow;
  /** false — выбран ровно один пловец: его имя стоит в чипе состава, в строках оно лишнее. */
  showNames: boolean;
  onClick: () => void;
}) {
  // Времени может не быть вовсе (полночь в источнике = «не назначено») — тогда плитка
  // с прочерком, пунктирная рамка и прямая подпись. Врать точным временем нельзя.
  const scheduled = row.startAt != null;
  const single = row.entries.length === 1 ? row.entries[0] : null;

  return (
    <button
      type="button"
      onClick={onClick}
      className="mb-2 grid w-full items-center gap-2.5 rounded-[12px] border p-2.5 text-left last:mb-0"
      style={{
        gridTemplateColumns: '74px minmax(0,1fr)',
        background: row.mine ? 'var(--theme-personal-bg)' : 'var(--deep-card-bg)',
        borderColor: row.mine ? 'var(--theme-personal-border)' : 'var(--deep-card-border)',
        borderStyle: scheduled ? 'solid' : 'dashed',
      }}
    >
      {/* Плитка времени — на всю высоту строки. */}
      <span
        className="flex h-full items-center justify-center rounded-[10px] py-2 text-[16px] font-black tabular-nums"
        style={{
          fontFamily: 'var(--deep-font-display)',
          background: row.mine ? 'var(--theme-personal-badge-bg)' : 'var(--deep-divider)',
        }}
      >
        {scheduled ? formatApproxTime(row.startAt) : '—'}
      </span>

      <span className="min-w-0">
        {/* Дисциплина плиткой того же компонента, что и на строке результата, + метка. */}
        <span className="flex items-center gap-2">
          <UI_SwimmStyleIcon
            styleName={row.styleName}
            styleType="icon-len"
            styleLen={row.distance}
            className="w-[54px] shrink-0"
          />
          <span className="min-w-0 flex-1 text-[12.5px] font-extrabold">
            {bandLabel(row.gender, row.ageBand)} · H{row.heat}
            {/* Посев показываем, только когда в строке один участник: у нескольких он
                у каждого свой, и одно число врало бы про остальных. */}
            {single && (single.seedTime
              ? <> · <span className="mr-0.5 text-[9px] uppercase opacity-60">seed</span><UI_SwimTime time={single.seedTime} /></>
              : ' · NT')}
            {!scheduled && <span className="ml-1 opacity-60">· not scheduled</span>}
            {single && <StatusBadge status={single.status} />}
          </span>
        </span>

        {/* Имена столбиком: по одному на строку, дорожка справа на уровне имени. */}
        {showNames && row.entries.map((e) => (
          <span key={e.id} className="mt-1 flex items-baseline justify-between gap-2">
            <span
              className={`min-w-0 flex-1 truncate text-[15px] ${e.mine ? 'font-black' : 'font-bold'}`}
              style={{
                textDecoration: e.status === 'no-show' ? 'line-through' : undefined,
                opacity: e.status === 'no-show' ? 0.6 : undefined,
              }}
              dir="auto"
            >
              {e.mine && '⭐ '}
              {e.name}
              {e.isRelay && <span className="ml-1 text-[10px] font-black" style={{ color: 'var(--deep-accent)' }}>· RELAY</span>}
              {e.leg && <span className="ml-1 text-[10px] font-black" style={{ color: 'var(--theme-personal-accent)' }}>{e.leg}</span>}
              {!e.mine && !e.isRelay && <span className="ml-1 text-[10px] font-black" style={{ color: 'var(--deep-accent)' }}>· CLUB</span>}
              {row.entries.length > 1 && <StatusBadge status={e.status} />}
            </span>
            <span
              className="shrink-0 text-[15px] font-black tabular-nums"
              style={{
                fontFamily: 'var(--deep-font-display)',
                color: e.mine ? 'var(--theme-personal-accent)' : 'var(--deep-accent)',
              }}
            >
              L{e.lane}
            </span>
          </span>
        ))}
      </span>
    </button>
  );
}

/**
 * Статус заявки. После старта стартовый протокол превращается в ответ «кто не вышел»
 * (правило исходного хендоффа), поэтому неявка помечается явно, а не прячется.
 */
function StatusBadge({ status }: { status: string }) {
  if (status === 'no-show') {
    return (
      <span
        className="ml-1.5 rounded-full px-2 py-[3px] text-[9px] font-black uppercase tracking-[.06em]"
        style={{ background: 'var(--deep-divider)', color: 'var(--deep-text-mute)' }}
      >
        no-show
      </span>
    );
  }
  if (status === 'swum') {
    return (
      <span
        className="ml-1.5 rounded-full px-2 py-[3px] text-[9px] font-black uppercase tracking-[.06em]"
        style={{ background: 'var(--deep-accent-soft)', color: 'var(--deep-accent)' }}
      >
        swum ✓
      </span>
    );
  }
  return null;
}
