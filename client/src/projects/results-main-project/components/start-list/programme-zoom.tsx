import React, { useEffect, useState } from 'react';
import NotPublished from './not-published';
import RefreshBar from './refresh-bar';
import { useStartListProgramme } from './use-start-list';
import { dayLabel, formatApproxTime, formatExactTime, sortEvents, swimLabel } from './start-list-helpers';

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
  const events = day ? sortEvents(day.events) : [];

  return (
    <div>
      <div className="mb-3 flex flex-wrap items-center justify-between gap-2">
        <div className="text-sm font-bold">{data.comp_name} · {data.entries} entries</div>
      </div>
      <RefreshBar updatedAt={data.updated_at} onRefresh={refresh} />
      {data.days.length > 1 && (
        <div className="mb-3 flex flex-wrap gap-1.5">
          {data.days.map((d, i) => (
            <button
              key={d.date}
              type="button"
              onClick={() => setDayIdx(i)}
              className={`rounded-full border px-3 py-1 text-xs font-bold ${i === dayIdx ? 'opacity-100' : 'opacity-60'}`}
              style={{ borderColor: 'var(--theme-mode-border-input)' }}
            >
              {dayLabel(d.date)}
            </button>
          ))}
        </div>
      )}
      <div className="divide-y" style={{ borderColor: 'var(--theme-mode-border-input)' }}>
        {events.map((e) => (
          <button
            key={e.org_discipline_id}
            type="button"
            onClick={() => onOpenEvent(e.org_discipline_id)}
            className="flex w-full items-center gap-3 py-2.5 text-left"
          >
            <div className="w-16 shrink-0 text-sm font-black">{formatApproxTime(e.start_at)}</div>
            <div className="w-10 shrink-0 text-center text-sm font-extrabold opacity-80">
              {e.event_number != null ? `#${e.event_number}` : '—'}
            </div>
            <div className="min-w-0 flex-1 text-sm">
              {swimLabel(e.distance, e.style_name)} {e.event_category ? `· ${e.event_category}` : ''}
            </div>
            <div className="shrink-0 text-xs opacity-70">{e.entries} entries</div>
            <div className="shrink-0 opacity-40">›</div>
          </button>
        ))}
        {events.length === 0 && <div className="py-4 text-center text-sm opacity-60">Nothing scheduled for this day.</div>}
      </div>
    </div>
  );
}
