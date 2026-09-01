import React from 'react';
import UI_SwimmStyleIcon from '../../../components/mix/swimm-style-icon/swimm-style-icon';
import UI_SwimTime from '../../../components/mix/swim-time/swim-time';
import RefreshBar from './refresh-bar';
import { useStartListSwimmerAcross } from './use-start-list';
import { dayLabel, formatApproxTime, swimLabel } from './start-list-helpers';

/**
 * Зум 3 — карточка одного пловца (§4.3 плана).
 *
 * ⚠ Временный экран: по принятому дизайну «3a Ticket + Following» его место занимает
 * карточка ПЛАНА (шаг Т6) — там же несколько выбранных, дни-чипы и золотой билет. Пока Т6
 * не сделан, этот экран остаётся главным ответом родителю и трогать его незачем.
 *
 * Ходит СРАЗУ ПО ВСЕМ источникам соревнования: у составного старта (окружные протоколы)
 * заплывы одного пловца лежат в разных compID, и «покажи мне все его заплывы» иначе не
 * ответить. Внутри — группировка по дням: «в какой день» — половина вопроса.
 */
export default function SwimmerZoom({ orgCompIds, swimmerId, onBack, onOpenHeat }: {
  orgCompIds: number[];
  swimmerId: number;
  onBack: () => void;
  onOpenHeat: (orgCompId: number, orgDisciplineId: number, heat: number) => void;
}) {
  const { data, loading, notFound, refresh } = useStartListSwimmerAcross(orgCompIds, swimmerId);

  if (loading && !data) return <div className="py-6 text-center text-sm opacity-60">Loading…</div>;
  if (notFound || !data) return <div className="py-6 text-center text-sm opacity-60">This swimmer has no entries here.</div>;

  // Дни в порядке выдачи (сервер уже отсортировал по дню и времени) — Map сохраняет
  // порядок вставки, поэтому отдельной сортировки тут не нужно.
  const byDay = new Map<string, typeof data.swims>();
  for (const s of data.swims) {
    const key = s.comp_date.slice(0, 10);
    (byDay.get(key) ?? byDay.set(key, []).get(key)!).push(s);
  }

  return (
    <div>
      <button type="button" onClick={onBack} className="mb-2 text-xs font-bold opacity-70 hover:opacity-100">← Programme</button>
      <div className="mb-1 text-lg font-black" dir="auto">{data.swimmer_name} · {data.birth_year} · {data.club_name}</div>
      <RefreshBar updatedAt={data.updated_at} onRefresh={refresh} />
      {data.first_start_at && (
        <div
          className="mb-4 rounded-[12px] p-3 text-sm font-bold"
          style={{ background: 'var(--theme-mode-surface)', border: '1px solid var(--theme-mode-border-input)' }}
        >
          {/* «Приезжать к» тут больше НЕТ: оно считалось от первого старта минус выдуманные
              45 минут. Настоящее время приезда — от разминки из регламента, и живёт оно на
              карточке плана (Т8), где для него есть данные. */}
          ⏱ First start {formatApproxTime(data.first_start_at)}
        </div>
      )}
      {[...byDay.entries()].map(([day, swims]) => (
        <div key={day} className="mb-4">
          {/* Подпись дня рисуется всегда, даже когда день один: без неё «когда» читается
              как «сегодня», а протокол публикуют за неделю до старта. */}
          <div className="mb-1.5 text-[12px] font-extrabold uppercase tracking-wide opacity-70">
            {dayLabel(day)}
          </div>
          {swims.map((s) => (
            <div
              key={s.id}
              onClick={() => onOpenHeat(s.org_comp_id, s.org_discipline_id, s.heat)}
              className="mb-2 flex cursor-pointer items-center gap-3 rounded-[12px] border p-3"
              style={{ borderColor: 'var(--theme-mode-border-input)' }}
            >
              {/* Дисциплину рисует общий компонент стиля — он же на строке результата.
                  А вот СТРОКУ здесь строим свою: `SwimRow` собран под результат (место,
                  медаль, очки, крупное время справа) и в стартовом протоколе показывал бы
                  то, чего ещё не было. Главное число тут — ВРЕМЯ СТАРТА. */}
              <UI_SwimmStyleIcon styleName={s.style_name} className="h-10 w-10 shrink-0" />
              <div className="min-w-0 flex-1">
                <div className="text-base font-black">{formatApproxTime(s.heat_start_at)}</div>
                <div className="text-xs opacity-70">
                  {swimLabel(s.distance, s.style_name)} · heat {s.heat} · lane {s.lane}
                </div>
              </div>
              <div className="shrink-0 text-right text-xs">
                {s.seed_time ? (
                  <>
                    <span className="mr-1 text-[9px] font-bold uppercase opacity-60">seed</span>
                    <UI_SwimTime time={s.seed_time} />
                  </>
                ) : 'NT'}
              </div>
            </div>
          ))}
        </div>
      ))}
    </div>
  );
}
