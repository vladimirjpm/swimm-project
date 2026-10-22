import React, { useEffect, useState } from 'react';
import UI_SwimmStyleIcon from '../../../components/mix/swimm-style-icon/swimm-style-icon';
import NotPublished from './not-published';
import RefreshBar from './refresh-bar';
import { useStartListProgramme } from './use-start-list';
import { bandLabel } from './plan-model';
import { formatApproxTime, formatExactTime, groupEventsByDiscipline, swimLabel } from './start-list-helpers';

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
/**
 * В каких заплывах (`org_discipline_id`) плывут «свои» — состав плана: ссылка `?plan=`, свой
 * сохранённый план или дефолт из избранного. Считает таб (те же данные, что у My plan).
 */
export interface FollowedEvents {
  /** Плывёт хотя бы один выбранный ПЛОВЕЦ — золотая точка. */
  swimmerEvents: ReadonlySet<number>;
  /** Плывёт кто-то из выбранного КЛУБА — синяя точка. */
  clubEvents: ReadonlySet<number>;
}

export default function ProgrammeZoom({ orgCompId, day, followed, startsLabel, notify, onPublished, onOpenEvent }: {
  orgCompId: number;
  followed: FollowedEvents;
  /** yyyy-MM-dd открытой сессии; null — первый день программы. */
  day: string | null;
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

  // День выбирают чипы СЕССИЙ в зоне фильтров (сессия = протокол × день, 21.10.2026), здесь
  // его только применяем. Своих дней-чипов у программы больше нет: они жили внутри экрана, и
  // в My plan выбранный день терялся.
  const shownDay = data.days.find((d) => d.date.slice(0, 10) === day) ?? data.days[0];
  const groups = shownDay ? groupEventsByDiscipline(shownDay.events) : [];

  return (
    <div>
      {/* Заголовок-счётчик («N entries · имя протокола») уехал в зону фильтров 5d — он
          нужен и в режиме плана, а тут был только на программе. */}
      <RefreshBar updatedAt={data.updated_at} onRefresh={refresh} />

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
              // Дистанция ПОД картинкой, а не поверх (решение Влада 21.10.2026): на рисунке
              // число спорило с силуэтом пловца и читалось хуже.
              lenPlacement="below"
              iconSize={72}
              lenSize={16}
              className="src-programme-zoom shrink-0"
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
              <span className="flex min-w-0 flex-1 items-center gap-1.5">
                <span className="min-w-0 truncate text-[14px] font-bold">{bandLabel(e.gender, e.age_band)}</span>
                <FollowDots
                  swimmer={followed.swimmerEvents.has(e.org_discipline_id)}
                  club={followed.clubEvents.has(e.org_discipline_id)}
                />
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

/**
 * Метки «здесь плывут свои» у строки программы (решение Влада 22.10.2026): золотая — пловец
 * из состава, синяя — кто-то из клуба состава. Цвета те же, что у чипов людей в зоне
 * фильтров: золото (`--deep-gold-bar` — графический токен, текстовый `--theme-personal-accent` в
 * светлой теме тёмно-оливковый и на точке читался как коричневый) и акцент клуба (`--deep-accent`).
 * Ни одной — ничего не рисуем: у гостя без состава программа выглядит как раньше.
 */
function FollowDots({ swimmer, club }: { swimmer: boolean; club: boolean }) {
  if (!swimmer && !club) return null;
  const label = [swimmer && 'your swimmer', club && 'your club'].filter(Boolean).join(' and ');
  return (
    <span className="flex flex-none items-center gap-1" title={`Swims here: ${label}`} aria-label={`Swims here: ${label}`}>
      {swimmer && <span className="h-2 w-2 rounded-full" style={{ background: 'var(--deep-gold-bar)' }} />}
      {club && <span className="h-2 w-2 rounded-full" style={{ background: 'var(--deep-accent)' }} />}
    </span>
  );
}
