import React from 'react';
// Строка привозит свои стили сама — ожог `EventPlate`: его CSS лежал в `swimmer-page.css`,
// и на чужой странице плита разъезжалась. Общий блок обязан быть самодостаточным.
import './swim-row.css';
import UI_SwimmerTimeCell from '../mix/swimmer-time-cell/swimmer-time-cell';
import { swimFlaggedRowProps } from '../mix/swim-time/swim-time';
import type { SwimQuality, SwimTimeDelta } from '../mix/swim-time/swim-time';
import UI_MedalIcon from '../mix/medal-icon/medal-icon';
import UI_PrelimLabel from '../mix/prelim-label/prelim-label';
import UI_SwimmStyleIcon from '../mix/swimm-style-icon/swimm-style-icon';
import UI_PoolIcon from '../mix/pool-icon/pool-icon';
import UI_DateIcon from '../mix/date-icon/date-icon';
import UI_SwimmerNameCell from '../mix/swimmer-name-cell/swimmer-name-cell';
import UI_NormativeLevelIcon from '../mix/normative-level-icon/normative-level-icon';
import UI_RankOfPeers from '../mix/rank-of-peers/rank-of-peers';
import Helper from '../../../utils/helpers/data-helper';

/**
 * `SwimRow` — ОДНА строка заплыва на весь продукт (план
 * `docs/plans/swim-row-shared-component-plan.md`).
 *
 * До неё «кто, что проплыл, за сколько, где и насколько это хорошо» было написано пять раз:
 * карточка спортсмена, `SwimmerResultRow`, таблицы Season best и Personal bests, список
 * `/season-best`. Пять копий молча теряли поведение (признак спорного времени, фоллбек
 * эмблемы клуба, формат даты) и расходились видом.
 *
 * Раскладка — эталон карточки спортсмена, две линии:
 *
 *   линия 1:  [место 52px]   [плитка стиля 86px]   [кто/где — тянется]   [время]
 *              медаль / #N     иконка + красная      имя пловца ИЛИ        + чип
 *              подпись         дистанция, под ней    название старта       SB/BEST/PB
 *              [prelim]        подпись бассейна
 *   ───────────────────────── border-top, отступ слева 46px ─────────────────────
 *   линия 2:  Points: 484 · 25 FEB 2026 · «старт»   [extras] [+2.73] [▶] [дуга уровня]
 *
 * Правила, которые нельзя потерять при переносе (§9 плана):
 *  • время — ТОЛЬКО через `UI_SwimmerTimeCell`/`UI_SwimTime`, и всегда вместе с `quality`
 *    (инвариант И11), носитель спорной строки — `swimFlaggedRowProps`;
 *  • эмблему клуба ищет `UI_SwimmerNameCell` по ОРИГИНАЛЬНОМУ имени клуба;
 *  • prelim-место — ранжир сессии, а не медаль;
 *  • дистанция бывает пятизначной («10000») и эстафетной («4X50») — плитку рисует
 *    `UI_SwimmStyleIcon`, у него это уже починено.
 *
 * ПАЛИТРА: цвета взяты из локальных токенов `--sr-*` с фоллбеком на палитру results
 * (`--theme-*`) — приём `FilterCard`. Deep-страницы переопределяют `--sr-*` НА СВОЁМ
 * КОНТЕЙНЕРЕ (`.deep-list`, `.sb-list`), а не на корне страницы: иначе перекрасится соседняя
 * вёрстка. Геометрия намеренно НЕ токенизирована — строка должна выглядеть одинаково везде.
 */

export type SwimRowBadge = 'sb' | 'best' | 'pb' | null;

export interface SwimRowPlace {
  /**
   * medal  — протокольное место (медаль или серый диск, см. `isAward`);
   * circle — место без награды (prelim, ранжир сессии);
   * rank   — место в сезонном рейтинге («#7»), равные времена ДЕЛЯТ место;
   * none   — места нет вовсе (личный рекорд).
   *
   * ⚠ Медаль и `#N` смешивать нельзя: первая за протокольное место, второй — за место
   * среди сверстников. Медальный кружок в рейтинге соврал бы.
   */
  kind: 'medal' | 'circle' | 'rank' | 'none';
  value?: number | string | null;
  /** Подпись под местом: «age 14». Пометку [prelim] рисует сам компонент по `heatType`. */
  caption?: string | null;
  /** medal: заплыв награждаемый — медаль цветная. Иначе тот же диск, но серый. */
  isAward?: boolean;
  /** rank: первое место — это и есть season best, красится золотом. */
  isFirst?: boolean;
  /**
   * rank: сколько всего ровесников в круге сравнения — подпись «of 36» ПОД местом.
   * Без неё «#1» не отличает первого из тридцати шести от первого из двух.
   */
  peerCount?: number | null;
}

export interface SwimRowLevel {
  /** Нормативы у мужчин и женщин разные; 'none' — пол неизвестен, уровня не будет. */
  gender: 'male' | 'female' | 'none';
  /** Возраст в сезоне; у мастерса хелпер сам разложит его в полосу («45» → «45-49»). */
  ageInSeason?: number | string | null;
  /** Запасная возрастная группа протокола («25-29»), если возраста строкой нет. */
  ageGroup?: string | null;
  /** Мастерс-старт: своя таблица нормативов. Без флага ветеран меряется юношеской шкалой. */
  isMasters?: boolean;
}

export interface SwimRowProps {
  // ── дисциплина
  stroke: string;
  /** Строкой: «50», «1500», «4X50» — эстафетные дистанции не числа. */
  distance: string;
  poolType?: string | null;
  /**
   * Плитка дисциплины. Выключается там, где дисциплина ОДНА на весь список и уже
   * названа шапкой (`/season-best`): пятьдесят одинаковых плиток отнимают место
   * у имени и ничего не говорят. Стиль и дистанция всё равно обязательны: по ним
   * считается дуга уровня.
   */
  showEvent?: boolean;

  // ── время (И11: время и его качество ездят вместе)
  time?: string | null;
  quality?: SwimQuality | null;
  splits?: string | null;
  timeFail?: boolean;
  timeFailNote?: string | null;
  /** SB сильнее BEST и ЗАМЕЩАЕТ его: одна строка не носит два чипа. */
  badge?: SwimRowBadge;

  place?: SwimRowPlace;
  /** 'prelim' | 'final' | null — пометку рисует `UI_PrelimLabel`, единственный её носитель. */
  heatType?: string | null;

  /** Кто плыл. Задан — на месте соревнования в линии 1 встаёт `UI_SwimmerNameCell`. */
  swimmer?: {
    name: string;
    club?: string | null;
    showClubIcon?: boolean;
  } | null;

  competition?: { name: string; isChampionship?: boolean } | null;
  /**
   * Где показать соревнование. По умолчанию: есть `swimmer` — во второй линии (его место в
   * первой занято именем), нет — в первой. Карточка спортсмена держит его во второй линии
   * рядом с датой и передаёт 'line2' явно.
   */
  meetPlacement?: 'line1' | 'line2';
  /** «DD/MM/YYYY» или ISO «YYYY-MM-DD» — формат вывода один, его держит `UI_DateIcon`. */
  date?: string | null;
  points?: number | null;

  /** Дуга уровня считается ВНУТРИ строки (одна реализация на продукт). null — не показывать. */
  level?: SwimRowLevel | null;

  /**
   * Отставание от лидера в мс. Печатается СРАЗУ ЗА ВРЕМЕНЕМ — это его свойство,
   * а не отдельная ячейка строки, и формат держит сам `UI_SwimTime` (`swimGapLabel`).
   */
  gapMs?: number | null;

  /**
   * Остальные сравнения с эталонами — туда же, под время: Δ клуб, Δ Израиль.
   * Пустые (эталона в данных нет) компонент времени отбрасывает сам.
   */
  deltas?: SwimTimeDelta[];

  /** Редкие ячейки второй линии: «of 12», Δ клуб, Δ Израиль, «2nd swim». */
  extras?: React.ReactNode;

  /** Вся строка — ссылка. Нет — рисуется <div>. */
  href?: string;
  /** Кнопка «есть видео» (пока передаёт только карточка спортсмена). */
  onOpenMedia?: (() => void) | null;
  className?: string;
}

/** «individual_medley» → «individual medley»: ключ стиля приходит машинным. */
export const swimRowStrokeLabel = (stroke?: string | null): string =>
  (stroke ?? '').replace(/_/g, ' ');

function PlaceCell({ place, heatType }: { place?: SwimRowPlace; heatType?: string | null }) {
  const kind = place?.kind ?? 'none';
  const value = place?.value;

  return (
    <div className="swim-row__place">
      {kind === 'medal' && (
        <UI_MedalIcon
          place={String(value ?? '')}
          styleType={place?.isAward ? 'icon-place' : 'icon-noplace'}
          styleSize="medal-40"
        />
      )}
      {kind === 'circle' && <span className="swim-row__circle">{value ?? '—'}</span>}
      {kind === 'rank' && (
        <UI_RankOfPeers
          rank={Number(value)}
          peerCount={place?.peerCount}
          isFirst={place?.isFirst}
          className={`swim-row__rank${place?.isFirst ? ' swim-row__rank--first' : ''}`}
          captionClassName="swim-row__caption"
        />
      )}
      {kind === 'none' && <span className="swim-row__circle swim-row__circle--empty">—</span>}
      <UI_PrelimLabel heatType={heatType} className="swim-row__caption" />
      {place?.caption && <span className="swim-row__caption">{place.caption}</span>}
    </div>
  );
}

function SwimRow({
  stroke,
  distance,
  poolType,
  showEvent = true,
  time,
  quality = null,
  splits,
  timeFail = false,
  timeFailNote = null,
  badge = null,
  place,
  heatType,
  swimmer,
  competition,
  meetPlacement,
  date,
  points,
  level = null,
  gapMs,
  deltas,
  extras,
  href,
  onOpenMedia,
  className = '',
}: SwimRowProps) {
  const flagged = swimFlaggedRowProps(quality);

  // Уровень считает клиент из NormativeStandard — вторая реализация на сервере разъехалась
  // бы с этой. Считаем ЗДЕСЬ, чтобы дуга была одинаковой на всех пяти поверхностях.
  const ageStr =
    level?.ageInSeason != null && level.ageInSeason !== '' ? String(level.ageInSeason) : null;
  const levelInfo =
    level && time
      ? Helper.getNormativeLevelInfo({
          gender: level.gender,
          poolType: Helper.resolvePoolType(poolType),
          styleName: stroke,
          distance: `${distance}m`,
          time: Helper.parseTimeToSeconds(time),
          isMaster: !!level.isMasters,
          ageGroup: level.ageGroup ?? ageStr,
          event_style_age: ageStr,
        })
      : null;

  // Помеченный заплыв не показывается как достижение (правило `UI_SwimTime`): ни чипа
  // BEST/SB/PB, ни дуги уровня. До объединения карточка спортсмена дугу всё-таки рисовала —
  // это расхождение, а не задумка.
  const showAchievements = !quality;

  const meetSide = meetPlacement ?? (swimmer ? 'line2' : 'line1');

  const hasLine2 =
    points != null ||
    !!date ||
    (meetSide === 'line2' && !!competition) ||
    !!extras ||
    !!onOpenMedia ||
    !!levelInfo;

  const body = (
    <>
      <div className="swim-row__line1">
        <PlaceCell place={place} heatType={heatType} />

        {showEvent && (
        <div
          className="swim-row__event"
          title={`${distance} ${swimRowStrokeLabel(stroke)}${poolType ? ` · ${poolType}` : ''}`}
        >
          {/* Белая подложка иконки — статусная, не зависит от темы: PNG нарисованы под
              светлый фон и в тёмной теме без плиты пропадают. */}
          <div className="swim-row__plate">
            <UI_SwimmStyleIcon
              styleName={stroke}
              styleLen={distance}
              styleType="icon-len"
              className="swim-row__plate-icon"
            />
          </div>
          {poolType && (
            <UI_PoolIcon
              styleType="icon-text-center"
              label={poolType}
              labelClassName="swim-row__pool"
            />
          )}
        </div>
        )}

        <div className="swim-row__mid">
          {swimmer ? (
            /* Блок «эмблема клуба + имя + клуб» — только общим компонентом: эмблему он ищет
               по ОРИГИНАЛЬНОМУ имени клуба через манифест, копия логики разъехалась бы на
               первом же новом клубе. RTL решает CSS (`unicode-bidi: plaintext`), а не `dir`
               на обёртке — тот разворачивал ячейку целиком и уносил эмблему вправо. */
            <UI_SwimmerNameCell
              firstName={swimmer.name}
              club={swimmer.club ?? undefined}
              showClubIcon={swimmer.showClubIcon}
              clubIconSide="left"
              clubIconWidth="10"
              firstLineClassName="swim-row__swimmer-name"
              secondLineClassName="swim-row__swimmer-club"
              nameBlockClassName="min-w-0 flex-1"
              className="min-w-0"
            />
          ) : (
            meetSide === 'line1' &&
            competition && (
              <span className="swim-row__meet-name" dir="auto" title={competition.name}>
                {competition.isChampionship && <span aria-hidden="true">🏆 </span>}
                {competition.name}
              </span>
            )
          )}
        </div>

        <div className="swim-row__time">
          <UI_SwimmerTimeCell
            time={time ?? '—'}
            quality={quality}
            qualityMarker="chip"
            time_split={splits ?? ''}
            time_fail={!!timeFail}
            time_fail_note={timeFailNote ?? null}
            gapMs={gapMs}
            deltas={deltas}
            gapClassName="swim-row__gap"
            firstLineClassName="swim-row__time-value"
            secondLineClassName="swim-row__time-sub"
            className="swim-row__time-cell"
          />
          {showAchievements && badge === 'best' && <span className="swim-row__chip">BEST</span>}
          {showAchievements && badge === 'pb' && <span className="swim-row__chip">PB</span>}
          {showAchievements && badge === 'sb' && (
            <span
              className="swim-row__chip swim-row__chip--sb"
              title="Fastest in the age group this season"
            >
              SB
            </span>
          )}
        </div>
      </div>

      {hasLine2 && (
        <div className="swim-row__line2">
          <div className="swim-row__line2-main">
            {points != null && (
              <div className="swim-row__points">
                Points: <strong>{points}</strong>
              </div>
            )}
            {(date || (meetSide === 'line2' && competition)) && (
              <div className="swim-row__when">
                {/* Формат даты в продукте живёт в одном месте — `UI_DateIcon`. */}
                {date && (
                  <UI_DateIcon
                    styleType="row-style-1"
                    date={date}
                    fontClassName="swim-row__date"
                  />
                )}
                {/* Длинные названия стартов обрезаются многоточием — полное остаётся в title. */}
                {meetSide === 'line2' && competition && (
                  <span className="swim-row__meet-inline" dir="auto" title={competition.name}>
                    {date ? '· ' : ''}
                    {competition.isChampionship && <span aria-hidden="true">🏆 </span>}
                    {competition.name}
                  </span>
                )}
              </div>
            )}
          </div>

          {extras && <span className="swim-row__extras">{extras}</span>}

          {onOpenMedia && (
            <button
              type="button"
              onClick={(e) => {
                // Строка целиком бывает ссылкой — иначе клик по кнопке уводил бы со страницы.
                e.preventDefault();
                e.stopPropagation();
                onOpenMedia();
              }}
              title="Open video"
              className="swim-row__media"
            >
              <svg width="13" height="13" viewBox="0 0 24 24" fill="#fff">
                <path d="M8 5v14l11-7z" />
              </svg>
            </button>
          )}

          <span className="swim-row__level">
            {levelInfo && showAchievements ? (
              <UI_NormativeLevelIcon
                levelName={levelInfo.currentLevel}
                styleType="gauge"
                styleSize="size-2"
                styleName={stroke}
                styleLen={distance}
                poolType={poolType ?? undefined}
                // Полоса мастерса под дугой («MS 45-49»): без неё непонятно, по какой шкале
                // считан разряд.
                isMasters={!!level?.isMasters}
                normativeAgeGroup={levelInfo.normativeAgeGroup}
                progressPercent={levelInfo.progressToNextLevel}
                nextTime={levelInfo.nextTime}
                disableClick={!!href}
              />
            ) : (
              <span className="swim-row__level-none">—</span>
            )}
          </span>
        </div>
      )}
    </>
  );

  const rootClass =
    `swim-row${showEvent ? '' : ' swim-row--no-event'}`
    + (quality ? ` ${flagged.className} swim-flagged-row--rounded swim-row--flagged` : '')
    + (className ? ` ${className}` : '');

  return href ? (
    <a {...flagged} href={href} className={rootClass}>
      {body}
    </a>
  ) : (
    <div {...flagged} className={rootClass}>
      {body}
    </div>
  );
}

export default SwimRow;
