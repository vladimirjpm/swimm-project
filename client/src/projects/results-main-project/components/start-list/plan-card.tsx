import React, { useMemo, useState } from 'react';
import RefreshBar from './refresh-bar';
import SwimRowD3 from './swim-row-d3';
import { useStartListClubSwims, useStartListProgrammes } from './use-start-list';
import {
  WARM_UP_LEAD_MINUTES, arriveByWarmUp, dayLabel, formatApproxTime, formatExactTime,
} from './start-list-helpers';
import { useAuth } from '../../../../hooks/useAuth';
import { assemblePlanSwims, firstSwimOfDay, groupPlanRows, planDays } from './plan-model';
import type { StartListClub, StartListSwimmer } from './types';
import type { StartListPlan } from './use-start-list-plan';

/**
 * Экран S2 — карточка плана, «билет» (шаг Т6, макет `Start List.dc.html`, секция 3a).
 *
 * Один экран и для «выбран один», и для «выбрано несколько»: вид меняется ДАННЫМИ, а не
 * разметкой — это прямое требование хендоффа, и оно же избавляет от двух расходящихся копий.
 *
 * Сверху вниз: дни-чипы → чипы состава → золотой билет (hero с первым стартом + строки
 * заплывов) → футер «времена приблизительные / Updated / Refresh».
 *
 * **ARRIVE BY** (§2 хендоффа, шаг Т8) — три условия сразу: соревнование ЧЕМПИОНАТ ∧ юзер
 * ЗАЛОГИНЕН ∧ отмечена галочка «I'm coming». Галочка при этом сама часть блока, поэтому
 * читаем правило так: чемпионат + логин открывают галочку, а время приезда появляется,
 * когда её отметили. Гостю и на обычном старте — только первый старт, без ARRIVE BY.
 *
 * Чего здесь пока нет и почему:
 * - **Share link** — шаг Т10.
 */
export default function PlanCard({
  orgCompIds, plan, swimmers, clubs, planLoading, shareUrl, shared, onChange, onEdit, onOpenHeat,
}: {
  /**
   * ВСЕ источники соревнования. Программу спрашиваем у каждого: у составного старта дни
   * лежат по разным протоколам (15/02, 16/02, 19/02 — четыре compID), и по первому из них
   * карточка показала бы один день из трёх, а остальные заплывы плана «потерялись бы».
   */
  orgCompIds: number[];
  /** ДЕЙСТВУЮЩИЙ состав (useEffectivePlan): сохранённый план либо дефолт из избранного. */
  plan: StartListPlan;
  /** Карточки выбранных пловцов и клубы соревнования — грузятся один раз на оба экрана. */
  swimmers: Record<number, StartListSwimmer>;
  clubs: StartListClub[];
  planLoading: boolean;
  /** Адрес этой же карточки с составом внутри — им делятся в родительском чате (Т10). */
  shareUrl: string;
  /** true — карточку открыли ПО ЧУЖОЙ ссылке: показываем её состав, свой план не трогаем. */
  shared: boolean;
  /** Состав целиком — крестик на чипе снимает выбранного, не заходя в пикер. */
  onChange: (next: StartListPlan) => void;
  onEdit: () => void;
  onOpenHeat: (orgCompId: number, orgDisciplineId: number, heat: number) => void;
}) {
  const { data: programmes, refresh } = useStartListProgrammes(orgCompIds);
  const { data: clubSwims, loading: clubSwimsLoading } = useStartListClubSwims(orgCompIds, plan.club_ids);

  const { isAuthenticated } = useAuth();

  // Дни соревнования — объединение дней всех протоколов, по возрастанию.
  const programmeDays = useMemo(
    () => [...new Set(programmes.flatMap((p) => p.days.map((d) => d.date.slice(0, 10))))].sort(),
    [programmes],
  );
  // «Обновлено» — самый свежий забор среди источников: протоколы тянут по одному.
  const updatedAt = useMemo(
    () => programmes.map((p) => p.updated_at).filter((v): v is string => v != null).sort().at(-1) ?? null,
    [programmes],
  );

  const swims = useMemo(
    () => assemblePlanSwims(swimmers, clubSwims, plan.swimmer_ids),
    [swimmers, clubSwims, plan.swimmer_ids.join(',')],
  );

  const days = useMemo(() => planDays(programmeDays, swims), [programmeDays, swims]);

  // Открытый день: первый, в который кто-то из выбранных вообще плывёт. Пустой день
  // открывать нечем — там будет пустой билет.
  const [openDay, setOpenDay] = useState<string | null>(null);
  const day = openDay ?? days.find((d) => d.swims > 0)?.date ?? days[0]?.date ?? null;

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

  const daySwims = swims.filter((s) => s.swim.comp_date.slice(0, 10) === day);
  // Строки D3: заплыв — одна строка, сколько бы выбранных в нём ни плыло (Т7).
  const dayRows = useMemo(() => groupPlanRows(daySwims), [daySwims]);
  const first = day ? firstSwimOfDay(swims, day) : null;
  const loading = planLoading || clubSwimsLoading;

  // Один выбранный — его имя стоит в чипе состава, и в строках его повторять незачем
  // (правило хендоффа: освободившееся место ничем не заполнять).
  const singleSwimmer = plan.swimmer_ids.length === 1 && plan.club_ids.length === 0;

  return (
    <div style={{ color: 'var(--deep-text)' }}>
      {/* Шапка: поделиться этим же составом ссылкой. Кнопка «назад» живёт над карточкой,
          на уровне таба, — второй такой же здесь была бы лишней. */}
      <div className="mb-2 flex items-center justify-between gap-2">
        {shared
          ? (
            <span className="text-[11px] font-extrabold uppercase tracking-wide" style={{ color: 'var(--deep-accent)' }}>
              Shared plan
            </span>
          )
          : <span />}
        <ShareButton url={shareUrl} />
      </div>

      {/* Открыли по чужой ссылке — честно говорим, чей это состав и как сделать его своим.
          Сам план получателя при этом НЕ перезаписывается (правило Т10). */}
      {shared && (
        <p className="mb-2 text-[12px]" style={{ color: 'var(--deep-text-mute)' }}>
          You’re looking at a plan someone shared with you. Tap “Edit” to make it yours.
        </p>
      )}

      {/* Дни-чипы. Гаснет ТОЛЬКО день, в который не плывёт НИКТО из выбранных. */}
      {days.length > 1 && (
        <div className="mb-2 flex flex-wrap gap-1.5">
          {days.map((d) => {
            const active = d.date === day;
            const empty = d.swims === 0;
            return (
              <button
                key={d.date}
                type="button"
                disabled={empty}
                onClick={() => setOpenDay(d.date)}
                className="rounded-[10px] border px-2.5 py-1.5 text-[11px] font-extrabold disabled:opacity-35"
                style={{
                  background: active ? 'var(--theme-personal-bg)' : 'var(--deep-card-bg)',
                  borderColor: active ? 'var(--theme-personal-border)' : 'var(--deep-card-border)',
                  color: active ? 'var(--theme-personal-accent)' : 'var(--deep-text)',
                }}
              >
                {dayLabel(d.date)}{d.swims > 0 ? ` · ${d.swims} swims` : ''}
              </button>
            );
          })}
        </div>
      )}

      {/* Чипы состава: кого показываем. Крестик снимает прямо отсюда — за этим не надо
          возвращаться в пикер. */}
      <div className="mb-3 flex flex-wrap items-center gap-1.5">
        {plan.swimmer_ids.map((id) => (
          <button
            key={`s${id}`}
            type="button"
            onClick={() => onChange({ ...plan, swimmer_ids: plan.swimmer_ids.filter((x) => x !== id) })}
            title="Remove from my plan"
            className="whitespace-nowrap rounded-full border px-2.5 py-1 text-[12.5px] font-extrabold"
            style={{
              background: 'var(--theme-personal-badge-bg)',
              borderColor: 'var(--theme-personal-border)',
              color: 'var(--theme-personal-accent)',
            }}
            dir="auto"
          >
            {swimmers[id]?.swimmer_name ?? `#${id}`}
            <span className="ml-1.5 opacity-60" aria-hidden>✕</span>
          </button>
        ))}
        {plan.club_ids.map((id) => {
          const club = clubs.find((c) => c.club_id === id);
          return (
            <button
              key={`c${id}`}
              type="button"
              onClick={() => onChange({ ...plan, club_ids: plan.club_ids.filter((x) => x !== id) })}
              title="Remove from my plan"
              className="whitespace-nowrap rounded-full border px-2.5 py-1 text-[12.5px] font-extrabold"
              style={{
                background: 'var(--deep-accent-soft)',
                borderColor: 'var(--deep-accent-border)',
                color: 'var(--deep-accent)',
              }}
              dir="auto"
            >
              {club ? `${club.club_name} · ${club.swimmers}` : `#${id}`}
              <span className="ml-1.5 opacity-60" aria-hidden>✕</span>
            </button>
          );
        })}
        <button
          type="button"
          onClick={onEdit}
          className="whitespace-nowrap rounded-full px-2.5 py-1 text-[12.5px] font-black"
          style={{ background: 'var(--deep-accent)', color: 'var(--deep-accent-ink)' }}
        >
          Edit ▾
        </button>
      </div>

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
              {daySwims.length > 0 ? 'No start time yet — the meet has not scheduled these heats.' : 'Nobody of yours swims on this day.'}
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
          {loading && daySwims.length === 0 && <div className="py-4 text-center text-sm opacity-60">Loading…</div>}
          {!loading && daySwims.length === 0 && (
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

/**
 * «Share link ↗». На телефоне отдаём системному «Поделиться» (ссылку шлют в родительский
 * чат — это и есть основной сценарий), на десктопе кладём в буфер.
 */
function ShareButton({ url }: { url: string }) {
  const [state, setState] = React.useState<'idle' | 'copied' | 'failed'>('idle');

  const share = async () => {
    try {
      if (navigator.share) {
        await navigator.share({ url });
        return;
      }
      await navigator.clipboard.writeText(url);
      setState('copied');
      window.setTimeout(() => setState('idle'), 2000);
    } catch {
      // Отмена системного диалога тоже приходит сюда — но врать «скопировано» нельзя.
      setState('failed');
      window.setTimeout(() => setState('idle'), 2000);
    }
  };

  return (
    <button
      type="button"
      onClick={share}
      className="shrink-0 rounded-full px-3 py-1.5 text-[11.5px] font-black"
      style={{ background: 'var(--deep-accent)', color: 'var(--deep-accent-ink)' }}
    >
      {state === 'copied' ? 'Link copied' : state === 'failed' ? 'Copy failed' : 'Share link ↗'}
    </button>
  );
}
