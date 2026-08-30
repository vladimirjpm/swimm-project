import React, { useEffect, useState } from 'react';
import UI_SwimmStyleIcon from '../../../components/mix/swimm-style-icon/swimm-style-icon';
import NotPublished from './not-published';
import RefreshBar from './refresh-bar';
import { useStartListProgramme } from './use-start-list';
import { bandLabel } from './plan-model';
import { dayLabel, formatApproxTime, formatExactTime, groupEventsByDiscipline, swimLabel } from './start-list-helpers';

/**
 * Зум 1 — программа соревнования по времени (§4.1 плана), умолчание таба.
 *
 * Остаётся и после редизайна (решение Влада 29.08.2026): карточка плана отвечает на «когда
 * плывёт МОЙ», а этот экран — на «что вообще плывут», и второй вопрос никуда не делся.
 *
 * Поиска по дисциплине здесь БОЛЬШЕ НЕТ (§4 хендоффа 29.08.2026): на экране было два поля
 * ввода рядом — по имени и по дисциплине, — и родитель регулярно печатал имя не в то.
 * Остался один поиск, по имени, в панели над зумами.
 */
export default function ProgrammeZoom({ orgCompId, startsLabel, notify, onPublished, onOpenEvent }: {
  orgCompId: number;
  /** «Sun 15 Feb» для экрана «ещё не опубликован»; null — дату отсюда не знаем. */
  startsLabel: string | null;
  /** Подписка «Notify me» на экране S3. onToggle=null — гость, кнопки нет. */
  notify: { isAuthenticated: boolean; notifyMe: boolean; onToggle: ((next: boolean) => void) | null };
  /** Сообщаем наверх, опубликован ли протокол: на «ещё нет» табу нечего предлагать
   *  (пикер пуст, следить не за кем), и вход в план там только мешает. */
  onPublished: (published: boolean) => void;
  onOpenEvent: (orgDisciplineId: number) => void;
}) {
  const { data, loading, notFound, refresh } = useStartListProgramme(orgCompId);
  const [dayIdx, setDayIdx] = useState(0);

  // «Last checked» — отметка КАЖДОЙ завершённой попытки, а не только успешной: экран S3
  // иначе выглядит так, будто кнопка «Check again» ничего не делает.
  const [lastChecked, setLastChecked] = useState<string | null>(null);
  useEffect(() => {
    if (!loading) setLastChecked(formatExactTime(new Date().toISOString()));
  }, [loading]);

  useEffect(() => {
    if (!loading) onPublished(!notFound && data != null);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [loading, notFound, data]);

  if (loading && !data) return <div className="py-6 text-center text-sm opacity-60">Loading…</div>;

  // Протокола ещё нет — это ожидаемое состояние источника, а не ошибка: посев делают за
  // несколько дней до старта (шаг Т9).
  if (notFound || !data) {
    return (
      <NotPublished
        startsLabel={startsLabel}
        isAuthenticated={notify.isAuthenticated}
        notifyMe={notify.notifyMe}
        lastChecked={lastChecked}
        onCheckAgain={refresh}
        onToggleNotify={notify.onToggle}
      />
    );
  }

  const day = data.days[dayIdx] ?? data.days[0];
  const groups = day ? groupEventsByDiscipline(day.events) : [];

  return (
    <div>
      <div className="mb-2 text-[12.5px] font-bold" style={{ color: 'var(--deep-text-mute)' }}>
        {data.comp_name} · {data.entries} entries
      </div>
      <RefreshBar updatedAt={data.updated_at} onRefresh={refresh} />
      {data.days.length > 1 && (
        <div className="mb-3 flex flex-wrap gap-1.5">
          {data.days.map((d, i) => (
            <button
              key={d.date}
              type="button"
              onClick={() => setDayIdx(i)}
              className="rounded-full border px-3 py-1 text-xs font-black"
              style={{
                borderColor: i === dayIdx ? 'var(--deep-accent-border)' : 'var(--deep-card-border)',
                background: i === dayIdx ? 'var(--deep-accent-soft)' : 'transparent',
                color: i === dayIdx ? 'var(--deep-accent)' : 'var(--deep-text-mute)',
              }}
            >
              {dayLabel(d.date)}
            </button>
          ))}
        </div>
      )}

      {/* Дисциплина = карточка, внутри — её возрасты. Оформление то же, что у строки плана
          (D3): плитка времени слева, метки справа — чтобы два экрана таба читались как один. */}
      {groups.map((g) => (
        <div
          key={g.key}
          className="mb-2 rounded-[12px] border p-2.5"
          style={{ background: 'var(--deep-card-bg)', borderColor: 'var(--deep-card-border)' }}
        >
          <div className="flex items-center gap-2">
            <UI_SwimmStyleIcon
              styleName={g.styleName}
              styleType="icon-len"
              styleLen={g.distance}
              className="w-[72px] shrink-0 text-[16px] sm:w-[84px] sm:text-[18px]"
            />
            {/* Заголовок переносится, а не обрезается: «100m individual medley» на узком
                экране не влезает, и «100m individual …» — худшее из двух зол. */}
            <span className="min-w-0 flex-1 text-[13px] font-extrabold leading-[1.25]">
              {swimLabel(g.distance, g.styleName)}
            </span>
            <span className="shrink-0 text-[11px] font-bold" style={{ color: 'var(--deep-text-mute)' }}>
              <span className="hidden sm:inline">
                {g.events.length} {g.events.length === 1 ? 'event' : 'events'} ·{' '}
              </span>
              {g.entries} entries
            </span>
          </div>

          {g.events.map((e) => (
            <button
              key={e.org_discipline_id}
              type="button"
              onClick={() => onOpenEvent(e.org_discipline_id)}
              className="mt-1.5 flex w-full items-baseline gap-2 text-left"
              dir="ltr"
            >
              <span
                className="w-[74px] shrink-0 text-[13px] font-black tabular-nums"
                style={{ fontFamily: 'var(--deep-font-display)' }}
              >
                {formatApproxTime(e.start_at)}
              </span>
              {/* Категория ПО-АНГЛИЙСКИ: `event_category` источника ивритский («בנות 11»),
                  и на витрину он не идёт — то же правило, что в строке плана. */}
              <span className="min-w-0 flex-1 truncate text-[14px] font-bold">
                {bandLabel(e.gender, e.age_band)}
              </span>
              <span className="shrink-0 text-[11px] font-bold" style={{ color: 'var(--deep-text-mute)' }}>
                {e.event_number != null ? `#${e.event_number} · ` : ''}{e.entries} entries
              </span>
              <span className="shrink-0 text-[13px] font-black" style={{ color: 'var(--deep-accent)' }}>›</span>
            </button>
          ))}
        </div>
      ))}

      {groups.length === 0 && (
        <div className="py-4 text-center text-sm" style={{ color: 'var(--deep-text-mute)' }}>
          Nothing scheduled for this day.
        </div>
      )}
    </div>
  );
}
