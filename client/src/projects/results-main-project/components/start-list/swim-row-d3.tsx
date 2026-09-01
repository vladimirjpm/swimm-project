import React from 'react';
import UI_SwimmStyleIcon from '../../../components/mix/swimm-style-icon/swimm-style-icon';
import UI_SwimTime from '../../../components/mix/swim-time/swim-time';
import { formatApproxTime } from './start-list-helpers';
import { bandLabel } from './plan-model';
import type { PlanRow } from './plan-model';

/**
 * Строка заплыва в карточке плана — формат **D3** (шаг Т7, хендофф §1.3).
 *
 * **Две раскладки одного и того же DOM**, переключаются на `sm:` (640px):
 *
 * - **Мобильная — вариант 4c** (задание Влада 31.08.2026): время полосой СВЕРХУ во всю
 *   ширину и крупно (30px), под ним строка «иконка стиля + категория/заплыв», дальше
 *   линейка-разделитель и участники. Узкий экран — основной вид таба, и плитка времени
 *   слева съедала на нём треть ширины у имён.
 * - **Широкая — как была**: сетка `74px | 1fr`, время плиткой слева во всю высоту.
 *
 * Раскладки НЕ разведены на два блока: данные и обработчики были бы продублированы, а
 * расходятся такие копии на первой же правке (прецедент — пять копий строки результата,
 * сведённых в `SwimRow`). Всё, что отличается, отличается классами `sm:`.
 *
 * **Правый столбик — «где в протоколе»**: категория, под ней `Heat N`, под ними дорожка
 * каждого участника — `Line 4`, на узком экране `L4`. Три подписи об одном и том же месте
 * стоят одной колонкой у правого края, а не растащены по строке.
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
    // `overflow-hidden` — ради мобильной полосы времени: она идёт до краёв карточки, и без
    // обрезки её углы вылезали бы за скругление рамки. На `sm:` возвращается сетка.
    <button
      type="button"
      onClick={onClick}
      className="mb-2 w-full overflow-hidden rounded-[12px] border text-left last:mb-0 sm:grid sm:items-center sm:gap-2.5 sm:overflow-visible sm:p-2.5"
      style={{
        gridTemplateColumns: '74px minmax(0,1fr)',
        background: row.mine ? 'var(--theme-personal-bg)' : 'var(--deep-card-bg)',
        borderColor: row.mine ? 'var(--theme-personal-border)' : 'var(--deep-card-border)',
        borderStyle: scheduled ? 'solid' : 'dashed',
      }}
    >
      {/* Время: полоса сверху на узком экране, плитка во всю высоту на широком. */}
      <span
        className="flex w-full items-center justify-center px-[14px] py-[7px] text-[30px] font-black leading-none tabular-nums sm:h-full sm:w-auto sm:rounded-[10px] sm:px-0 sm:py-2 sm:text-[16px] sm:leading-normal"
        style={{
          fontFamily: 'var(--deep-font-display)',
          background: row.mine ? 'var(--theme-personal-badge-bg)' : 'var(--deep-divider)',
        }}
      >
        {scheduled ? formatApproxTime(row.startAt) : '—'}
      </span>

      {/* Контентный блок: на узком экране у него свои поля (12/16), на широком их даёт
          padding самой карточки. */}
      <span className="block min-w-0 px-4 py-3 sm:p-0">
        {/* Шапка: слева дисциплина той же плиткой, что и на строке результата, справа —
            верх правого столбика (категория + заплыв). */}
        <span className="flex items-stretch gap-2 sm:items-center">
          <UI_SwimmStyleIcon
            styleName={row.styleName}
            styleType="icon-len"
            styleLen={row.distance}
            // Дистанция СПРАВА от иконки, а не поверх неё (решение Влада 31.08.2026): на
            // плитке этой ширины число ложилось прямо на пловца и спорило с рисунком.
            lenPlacement="right"
            // Кегль подписи задан в компоненте как 1.25em от плитки, поэтому размер числа
            // задаётся здесь, вместе с шириной. На УЗКОМ экране он вдвое крупнее (28px →
            // «100» рисуется 35px): там иконка идёт во всю ширину, и дистанция читается
            // наравне с категорией. На широком остаётся прежним.
            className="w-[104px] shrink-0 text-[28px] sm:w-[84px] sm:text-[16px]"
          />
          {!scheduled && (
            <span className="min-w-0 truncate text-[11px] font-bold opacity-60">not scheduled</span>
          )}
          {/* Правый столбик на узком экране тянется во всю высоту строки с иконкой:
              категория стоит по центру, `Heat N` прижат к низу (правка 31.08.2026).
              На широком остаётся прежняя пара строк подряд. */}
          <span className="ml-auto flex shrink-0 flex-col items-end whitespace-nowrap leading-[1.15]">
            <span className="my-auto text-[12.5px] font-black sm:my-0 sm:font-extrabold">
              {bandLabel(row.gender, row.ageBand)}
            </span>
            <span className="text-[11px] font-bold opacity-70">Heat {row.heat}</span>
          </span>
        </span>

        {/* Линейка между дисциплиной и участниками — только на узком экране: там строка
            иконки идёт во всю ширину и без неё имена липнут к ней. На широком роль
            разделителя играет сама сетка. */}
        <span
          className="mb-[2px] mt-[10px] block h-px sm:hidden"
          style={{ background: 'var(--deep-card-border)' }}
        />

        {/* Участники столбиком. Между разными участниками зазор больше, чем внутри одного:
            иначе имя второго читается продолжением первого. */}
        {row.entries.map((e, i) => (
          <React.Fragment key={e.id}>
          <span
            className={`flex items-center gap-[10px] py-[9px] sm:items-baseline sm:gap-2 sm:py-0 ${i === 0 ? 'sm:mt-1.5' : 'sm:mt-3'}`}
            dir="ltr"
          >
            {/* Метка «чей это пловец» — ОТДЕЛЬНАЯ колонка, а не приписка внутри имени.
                Имена ивритские: значок внутри строки имени двунаправленный алгоритм
                уносит на противоположный край — звезда оказывалась справа от имени, а
                «CLUB» слева. Своя колонка фиксированной ширины держит метки на одном
                месте при любом языке имени и выравнивает начала имён столбиком. */}
            <span
              className={`w-[34px] shrink-0 font-black uppercase tracking-[.04em] ${e.mine ? 'text-[13px] leading-none' : 'text-[10px]'}`}
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
            <span className="flex min-w-0 flex-1 flex-col gap-px sm:flex-row sm:items-baseline sm:gap-2">
              {showNames && (
                <span
                  // Имя на узком экране крупнее на 30% (15 → 19.5px): это главное слово
                  // строки, а места под него там больше — посев ушёл на строку ниже.
                  className={`min-w-0 truncate text-[19.5px] sm:text-[15px] ${e.mine ? 'font-black' : 'font-extrabold'}`}
                  style={{
                    textDecoration: e.status === 'no-show' ? 'line-through' : undefined,
                    opacity: e.status === 'no-show' ? 0.6 : undefined,
                  }}
                  dir="auto"
                >
                  {e.name}
                </span>
              )}
              {/* Посев: на узком экране подпись приглушена, а само время — основным цветом
                  (макет 4c). На широком остаётся как было — вся пара в 70% прозрачности. */}
              <span className="shrink-0 text-[10.5px] font-bold text-[color:var(--deep-text-mute)] sm:text-[11.5px] sm:text-[color:inherit] sm:opacity-70">
                {e.seedTime
                  ? (
                    <>
                      <span className="mr-1 uppercase sm:mr-0.5 sm:text-[9px] sm:opacity-80">seed</span>
                      <span className="font-extrabold tabular-nums text-[color:var(--deep-text)] sm:font-bold sm:text-[color:inherit]">
                        <UI_SwimTime time={e.seedTime} />
                      </span>
                    </>
                  )
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
              className="mt-1 block pl-[44px] text-[12.5px] font-bold leading-[1.5] sm:pl-[42px]"
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
