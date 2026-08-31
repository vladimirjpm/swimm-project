import React, { useMemo } from 'react';
import RefreshBar from './refresh-bar';
import SwimRowD3 from './swim-row-d3';
import { useStartListProgrammes } from './use-start-list';
import {
  WARM_UP_LEAD_MINUTES, arriveByWarmUp, dayLabel, formatApproxTime, formatExactTime,
} from './start-list-helpers';
import { useAuth } from '../../../../hooks/useAuth';
import { firstSwimOfDay, groupPlanRows } from './plan-model';
import type { PlanSwim } from './plan-model';
import type { StartListPlan } from './use-start-list-plan';

/**
 * Экран S2 — карточка плана, «билет» (шаг Т6, макет `Start List.dc.html`, секция 3a).
 *
 * Один экран и для «выбран один», и для «выбрано несколько»: вид меняется ДАННЫМИ, а не
 * разметкой — это прямое требование хендоффа, и оно же избавляет от двух расходящихся копий.
 *
 * Сверху вниз: золотой билет (hero с первым стартом + строки заплывов) → футер «времена
 * приблизительные / Updated / Refresh».
 *
 * Дни-чипы, чипы состава и Share уехали в ЗОНУ ФИЛЬТРОВ (5d) — она общая на оба режима
 * таба, и дни там стали чипами СЕССИЙ: у окружного чемпионата на одну дату приходится
 * два протокола, и дата без номера их не различала. Отсюда `activeOrgCompId` вместо
 * своего состояния «открытый день».
 *
 * **ARRIVE BY** (§2 хендоффа, шаг Т8) — три условия сразу: соревнование ЧЕМПИОНАТ ∧ юзер
 * ЗАЛОГИНЕН ∧ отмечена галочка «I'm coming». Галочка при этом сама часть блока, поэтому
 * читаем правило так: чемпионат + логин открывают галочку, а время приезда появляется,
 * когда её отметили. Гостю и на обычном старте — только первый старт, без ARRIVE BY.
 *
 */
export default function PlanCard({
  orgCompIds, plan, swims, activeOrgCompId, loading, shared, onChange, onOpenHeat,
}: {
  /**
   * ВСЕ источники соревнования. Программу спрашиваем у каждого: у составного старта дни
   * лежат по разным протоколам (15/02, 16/02, 19/02 — четыре compID), и по первому из них
   * карточка показала бы один день из трёх, а остальные заплывы плана «потерялись бы».
   */
  orgCompIds: number[];
  /** ДЕЙСТВУЮЩИЙ состав (useEffectivePlan): сохранённый план либо дефолт из избранного. */
  plan: StartListPlan;
  /** Собранные заплывы плана — считает таб (их число нужно и чипам сессий в зоне фильтров). */
  swims: PlanSwim[];
  /** Открытая сессия (протокол) — общий выбор с чипами зоны фильтров. */
  activeOrgCompId: number;
  loading: boolean;
  /** true — карточку открыли ПО ЧУЖОЙ ссылке: показываем её состав, свой план не трогаем. */
  shared: boolean;
  /** Только галочка «I'm coming»: состав правят чипами в зоне фильтров. */
  onChange: (next: StartListPlan) => void;
  onOpenHeat: (orgCompId: number, orgDisciplineId: number, heat: number) => void;
}) {
  const { data: programmes, refresh } = useStartListProgrammes(orgCompIds);

  const { isAuthenticated } = useAuth();

  // «Обновлено» — самый свежий забор среди источников: протоколы тянут по одному.
  const updatedAt = useMemo(
    () => programmes.map((p) => p.updated_at).filter((v): v is string => v != null).sort().at(-1) ?? null,
    [programmes],
  );

  // Заплывы ОТКРЫТОЙ СЕССИИ. Сессию выбирают чипами в зоне фильтров — здесь её только
  // применяем.
  const sessionSwims = useMemo(
    () => swims.filter((s) => s.swim.org_comp_id === activeOrgCompId),
    [swims, activeOrgCompId],
  );

  // День сессии — из её же заплывов: он подписывает hero и выбирает разминку. У протокола
  // федерации день один, но берём его из данных, а не из привязки: у сшитых источников
  // дата привязки и дата заплывов расходились.
  const day = sessionSwims[0]?.swim.comp_date.slice(0, 10) ?? null;

  // Справка о старте (Т1): чемпионат — хотя бы по одному протоколу; разминка — своя у дня.
  const isChampionship = programmes.some((p) => p.is_championship);
  const warmUpAt = useMemo(() => programmes
    .flatMap((p) => p.days)
    .find((d) => d.date.slice(0, 10) === day && d.warm_up_at)?.warm_up_at ?? null,
    [programmes, day]);
  const arriveBy = arriveByWarmUp(warmUpAt);
  // Галочку показываем, когда есть чему её включать: чемпионат, вход и введённая разминка.
  const canArrive = isChampionship && isAuthenticated && warmUpAt != null;
  const showArrive = canArrive && plan.im_coming;

  // Строки D3: заплыв — одна строка, сколько бы выбранных в нём ни плыло (Т7).
  const dayRows = useMemo(() => groupPlanRows(sessionSwims), [sessionSwims]);
  const first = day ? firstSwimOfDay(sessionSwims, day) : null;

  // Один выбранный — его имя стоит в чипе состава, и в строках его повторять незачем
  // (правило хендоффа: освободившееся место ничем не заполнять).
  const singleSwimmer = plan.swimmer_ids.length === 1 && plan.club_ids.length === 0;

  return (
    <div style={{ color: 'var(--deep-text)' }}>
      {/* Метка чужого состава. Share уехал в зону фильтров — он один на оба режима таба
          и стоит там на одном и том же месте. */}
      {shared && (
        <div className="mb-2 text-[11px] font-extrabold uppercase tracking-wide" style={{ color: 'var(--deep-accent)' }}>
          Shared plan
        </div>
      )}

      {/* Открыли по чужой ссылке — честно говорим, чей это состав и как сделать его своим.
          Сам план получателя при этом НЕ перезаписывается (правило Т10). */}
      {shared && (
        <p className="mb-2 text-[12px]" style={{ color: 'var(--deep-text-mute)' }}>
          You’re looking at a plan someone shared with you. Tap “Edit” to make it yours.
        </p>
      )}

      {/* Билет. Перфорация делит «когда приходить» и «что плывём» — она же граница между
          ответом на главный вопрос и подробностями. */}
      <div
        className="overflow-hidden rounded-[18px] border"
        style={{ background: 'var(--theme-personal-bg)', borderColor: 'var(--theme-personal-border)' }}
      >
        <div className="px-4 pt-4 pb-3">
          <div className="text-[9.5px] font-black uppercase tracking-[.09em]" style={{ color: 'var(--theme-personal-accent)' }}>
            {plan.swimmer_ids.length + plan.club_ids.length > 1 ? 'First of yours' : 'First start'}
            {day ? ` · ${dayLabel(day)}` : ''}
          </div>
          <div className="mt-1 flex items-start justify-between gap-3">
            <div
              className="text-[44px] font-black leading-none tabular-nums"
              style={{ fontFamily: 'var(--deep-font-display)' }}
            >
              {first ? formatApproxTime(first.swim.heat_start_at) : '—'}
            </div>
            {/* Плитка «во сколько приезжать» — главный ответ тому, кто едет с ребёнком. */}
            {showArrive && arriveBy && (
              <div
                className="shrink-0 rounded-[12px] px-3 py-2 text-center"
                style={{ background: 'var(--theme-personal-badge-bg)' }}
              >
                <div className="text-[9.5px] font-black uppercase tracking-[.09em]" style={{ color: 'var(--theme-personal-accent)' }}>
                  Arrive by
                </div>
                <div
                  className="text-[20px] font-black tabular-nums"
                  style={{ fontFamily: 'var(--deep-font-display)', color: 'var(--theme-personal-accent)' }}
                >
                  {arriveBy}
                </div>
              </div>
            )}
          </div>

          {/* Строка разминки + галочка. Время разминки — точное (из регламента), поэтому
              без «≈»: в отличие от заплывов, оно не плывёт на 20–40 минут. */}
          {canArrive && (
            <div className="mt-2 flex flex-wrap items-center justify-between gap-2">
              <span className="text-[12px] font-bold">
                Warm-up from {formatExactTime(warmUpAt)} — arrival is {WARM_UP_LEAD_MINUTES} min before.
              </span>
              <label className="flex shrink-0 cursor-pointer items-center gap-1.5 text-[12px] font-extrabold">
                <input
                  type="checkbox"
                  checked={plan.im_coming}
                  onChange={(e) => onChange({ ...plan, im_coming: e.target.checked })}
                  className="h-4 w-4 accent-current"
                  style={{ accentColor: 'var(--theme-personal-accent)' }}
                />
                I'm coming
              </label>
            </div>
          )}
          {!first && !loading && (
            <div className="mt-1 text-[12px] opacity-70">
              {sessionSwims.length > 0 ? 'No start time yet — the meet has not scheduled these heats.' : 'Nobody of yours swims on this day.'}
            </div>
          )}
        </div>

        {/* Перфорация + полукруги по краям, как на билете. Цвет полукругов — фон СТРАНИЦЫ
            (`--theme-mode-page-bg`), а не `--deep-page-bg`: билет лежит на карточке
            соревнования, и «вырез» должен совпадать с тем, что под ним. */}
        <div className="relative" style={{ borderTop: '2px dashed var(--theme-personal-border)' }}>
          <span className="absolute -left-[9px] -top-[9px] h-[18px] w-[18px] rounded-full" style={{ background: 'var(--theme-mode-page-bg)', borderRight: '1px solid var(--theme-personal-border)' }} />
          <span className="absolute -right-[9px] -top-[9px] h-[18px] w-[18px] rounded-full" style={{ background: 'var(--theme-mode-page-bg)', borderLeft: '1px solid var(--theme-personal-border)' }} />
        </div>

        <div className="p-3">
          {loading && sessionSwims.length === 0 && <div className="py-4 text-center text-sm opacity-60">Loading…</div>}
          {!loading && sessionSwims.length === 0 && (
            <div className="py-4 text-center text-sm opacity-60">Nothing on this day.</div>
          )}
          {dayRows.map((row) => (
            <SwimRowD3
              key={row.key}
              row={row}
              // Выбран ровно один пловец — его имя стоит в чипе состава; в строках его
              // не повторяем (правило хендоффа: освободившееся место ничем не заполнять).
              showNames={!singleSwimmer || row.entries.length > 1}
              onClick={() => onOpenHeat(row.orgCompId, row.orgDisciplineId, row.heat)}
            />
          ))}
        </div>
      </div>

      {/* Футер: время приблизительное — это правило витрины, а не оговорка. */}
      <p className="mt-2 text-[11px] opacity-70">
        Times are approximate — heats run 20–40 min off schedule.
      </p>
      <RefreshBar updatedAt={updatedAt} onRefresh={refresh} />
    </div>
  );
}
