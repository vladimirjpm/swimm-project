import React from 'react';
import UI_SwimmStyleIcon from '../../../components/mix/swimm-style-icon/swimm-style-icon';
import UI_SwimTime from '../../../components/mix/swim-time/swim-time';
import { formatApproxTime } from './start-list-helpers';
import { bandLabel } from './plan-model';
import type { PlanRow } from './plan-model';

/**
 * Строка заплыва в карточке плана — формат **D3** (шаг Т7, хендофф §1.3).
 *
 * Сетка `74px | 1fr`: слева плитка времени во всю высоту, справа — содержимое заплыва.
 * Главное число строки — ВРЕМЯ СТАРТА, поэтому оно и стоит отдельной плиткой; номер
 * заплыва в неё не идёт, иначе плитка читается как два числа сразу.
 *
 * **Правый столбик — «где в протоколе»** (правка 30.08.2026): сверху категория, под ней
 * `Heat N`, под ними дорожка каждого участника — `Line 4`, на узком экране `L4`. Три
 * подписи об одном и том же месте стоят одной колонкой у правого края, а не растащены по
 * строке: глаз ищет «мой заплыв, моя дорожка» в одном месте.
 *
 * **Посев — у ИМЕНИ, а не в шапке строки**: он у каждого участника свой, и одно число в
 * шапке врало бы про остальных. На узком экране посев уходит под имя, на широком стоит
 * следом за ним.
 *
 * Почему не общий `SwimRow`: он собран под результат (место, медаль, очки, дуга уровня) и
 * в стартовом протоколе показывал бы то, чего ещё не произошло — правило №2 исходного
 * хендоффа, откат 28.08.2026. Общими остаются части: `UI_SwimmStyleIcon`, `UI_SwimTime`.
 *
 * Несколько выбранных в одном заплыве — ОДНА строка: время и дисциплина общие, участники
 * идут столбиком с увеличенным зазором, у каждого своя дорожка (склейка в `groupPlanRows`).
 */
export default function SwimRowD3({ row, showNames, onClick }: {
  row: PlanRow;
  /** false — выбран ровно один пловец: его имя стоит в чипе состава, в строках оно лишнее.
   *  Посев и дорожка остаются: это про заплыв, а не про то, кто в нём. */
  showNames: boolean;
  onClick: () => void;
}) {
  // Времени может не быть вовсе (полночь в источнике = «не назначено») — тогда плитка
  // с прочерком, пунктирная рамка и прямая подпись. Врать точным временем нельзя.
  const scheduled = row.startAt != null;

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
        {/* Шапка: слева дисциплина той же плиткой, что и на строке результата, справа —
            верх правого столбика (категория + заплыв). */}
        <span className="flex items-center gap-2">
          <UI_SwimmStyleIcon
            styleName={row.styleName}
            styleType="icon-len"
            styleLen={row.distance}
            // Кегль подписи дистанции в компоненте задан в em (1.25em), поэтому размер
            // числа задаётся здесь — вместе с шириной плитки, иначе «100» на увеличенной
            // иконке выглядит потерянным.
            className="w-[96px] shrink-0 text-[18px] sm:w-[84px] sm:text-[20px]"
          />
          {!scheduled && (
            <span className="min-w-0 truncate text-[11px] font-bold opacity-60">not scheduled</span>
          )}
          <span className="ml-auto flex shrink-0 flex-col items-end leading-[1.15]">
            <span className="text-[12.5px] font-extrabold">{bandLabel(row.gender, row.ageBand)}</span>
            <span className="text-[11px] font-bold opacity-70">Heat {row.heat}</span>
          </span>
        </span>

        {/* Участники столбиком. Между разными участниками зазор больше, чем внутри одного:
            иначе имя второго читается продолжением первого. */}
        {row.entries.map((e, i) => (
          <React.Fragment key={e.id}>
          <span className={`flex items-baseline gap-2 ${i === 0 ? 'mt-1.5' : 'mt-3'}`} dir="ltr">
            {/* Метка «чей это пловец» — ОТДЕЛЬНАЯ колонка, а не приписка внутри имени.
                Имена ивритские: значок внутри строки имени двунаправленный алгоритм
                уносит на противоположный край — звезда оказывалась справа от имени, а
                «CLUB» слева. Своя колонка фиксированной ширины держит метки на одном
                месте при любом языке имени и выравнивает начала имён столбиком. */}
            <span
              className={`w-[34px] shrink-0 font-black uppercase ${e.mine ? 'text-[13px] leading-none' : 'text-[10px]'}`}
              style={{
                color: e.mine ? 'var(--theme-personal-accent)' : 'var(--deep-accent)',
                // Оптическая правка: у эмодзи ⭐ своя боковая пазуха внутри квадрата, из-за
                // неё звезда стоит на пару пикселей правее буквы «C» в «CLUB» при одинаковых
                // коробках. Сдвигаем ЗНАК, а не ячейку, иначе разъедутся начала имён.
                textIndent: e.mine ? '-3px' : undefined,
              }}
            >
              {e.mine ? '⭐' : e.isRelay ? '' : 'CLUB'}
            </span>

            {/* Имя и посев: на широком экране в строку, на узком посев уходит под имя. */}
            <span className="flex min-w-0 flex-1 flex-col sm:flex-row sm:items-baseline sm:gap-2">
              {showNames && (
                <span
                  className={`min-w-0 truncate text-[15px] ${e.mine ? 'font-black' : 'font-bold'}`}
                  style={{
                    textDecoration: e.status === 'no-show' ? 'line-through' : undefined,
                    opacity: e.status === 'no-show' ? 0.6 : undefined,
                  }}
                  dir="auto"
                >
                  {e.name}
                </span>
              )}
              <span className="shrink-0 text-[11.5px] font-bold opacity-70">
                {e.seedTime
                  ? <><span className="mr-0.5 text-[9px] uppercase opacity-80">seed</span><UI_SwimTime time={e.seedTime} /></>
                  : 'NT'}
              </span>
            </span>

            {/* Эстафетные пометки и статус — тоже отдельными ячейками: внутри имени их
                разворачивало ровно так же. */}
            {e.isRelay && <span className="shrink-0 text-[10px] font-black" style={{ color: 'var(--deep-accent)' }}>RELAY</span>}
            <StatusBadge status={e.status} />

            {/* Дорожка — низ правого столбика, ровно под «Heat N». На узком экране слово
                рядом с именем не помещается, поэтому там короткое «L4». */}
            <span
              className="shrink-0 text-[15px] font-black tabular-nums"
              style={{
                fontFamily: 'var(--deep-font-display)',
                color: e.mine ? 'var(--theme-personal-accent)' : 'var(--deep-accent)',
              }}
            >
              <span className="hidden sm:inline">Line </span>
              <span className="sm:hidden">L</span>
              {e.lane}
            </span>
          </span>

          {/* Состав эстафеты — сразу под своей командой, с отступом под её названием
              (вариант A, решение Влада 30.08.2026). Свой пловец выделен золотом: вопрос
              «где мой в этой четвёрке» закрывается им, а не номером ноги, которого мы не
              знаем. Меньше двух имён не показываем — см. `PlanRowEntry.members`.
              Контейнер `dir="ltr"`, направление — у каждого имени своё: перечисление
              должно идти в порядке состава, а не разворачиваться по первому имени. */}
          {e.members.length > 1 && (
            <span
              className="mt-1 block pl-[42px] text-[12.5px] font-bold leading-[1.5]"
              style={{ color: 'var(--deep-text-mute)' }}
              dir="ltr"
            >
              {e.members.map((m, i) => (
                <React.Fragment key={m.id}>
                  {i > 0 && <span className="px-[5px] opacity-40">·</span>}
                  {/* nowrap на КАЖДОМ имени: без него узкий экран рвёт перенос внутри имени,
                      и двунаправленный алгоритм разносит обрывки по разным краям строки —
                      «כרם» оказывается у одного имени, «אברמוביץ» у другого. Переносим
                      только между именами. */}
                  <span
                    className="whitespace-nowrap"
                    dir="auto"
                    style={m.mine ? { color: 'var(--theme-personal-accent)', fontWeight: 900 } : undefined}
                  >
                    {m.name}
                  </span>
                </React.Fragment>
              ))}
            </span>
          )}
          </React.Fragment>
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
        className="shrink-0 rounded-full px-2 py-[3px] text-[9px] font-black uppercase tracking-[.06em]"
        style={{ background: 'var(--deep-divider)', color: 'var(--deep-text-mute)' }}
      >
        no-show
      </span>
    );
  }
  if (status === 'swum') {
    return (
      <span
        className="shrink-0 rounded-full px-2 py-[3px] text-[9px] font-black uppercase tracking-[.06em]"
        style={{ background: 'var(--deep-accent-soft)', color: 'var(--deep-accent)' }}
      >
        swum ✓
      </span>
    );
  }
  return null;
}
