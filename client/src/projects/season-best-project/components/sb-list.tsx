import React from 'react';
import SwimRow from '../../components/swim-row/swim-row';
import { routes } from '../../../utils/routes';
import type { SeasonBestListItem } from '../../../hooks/useSeasonBestList';
import type { SeasonBestModules } from '../season-best-modules';

/**
 * Список среза — «кто быстрее всех» в связке стиль × дистанция × бассейн × возраст × пол.
 *
 * Строку рисует ОБЩИЙ `SwimRow` — та же самая, что в карточке спортсмена и на странице
 * пловца (план `docs/plans/swim-row-shared-component-plan.md`). Своей вёрстки у списка
 * больше нет: она и была одной из пяти копий, которые молча расходились.
 *
 * Особенность среза: строки про РАЗНЫХ людей, поэтому слот идентичности в первой линии
 * занимает имя пловца, а соревнование с датой уезжает во вторую (решение Влада §7 п.3 —
 * ровно так уже было сделано на мобильной ширине).
 *
 * Дедупа по пловцу нет: один человек законно занимает и первое место, и третье. Повтор
 * помечается номером попытки («2nd swim»), а не прячется.
 *
 * Строки-заголовка с подписями колонок здесь больше нет (решение Влада §7 п.2): у карточек
 * нет колонок, к которым заголовок мог бы прилипнуть. Что показывает список, говорит
 * строка-подсказка над ним (`sb-listhead__hint`).
 */

/** «2nd swim» / «3rd swim» — какой это по счёту заплыв пловца в списке. */
function attemptLabel(attempt: number): string {
  const suffix = attempt === 2 ? 'nd' : attempt === 3 ? 'rd' : 'th';
  return `${attempt}${suffix} swim`;
}

interface RowProps {
  row: SeasonBestListItem;
  modules: SeasonBestModules;
  /** Дисциплина среза — одна на весь список, в самих строках её нет. */
  stroke: string;
  distance: string;
  /**
   * Мастерский срез — у разряда своя таблица нормативов с возрастными полосами.
   * Определяется СРЕЗОМ (задана ли группа в фильтрах), а НЕ наличием `age_group` в строке:
   * группа протокола есть и у детских заплывов («11-12»), и по ней дуга уходила в чужую шкалу.
   */
  isMasters: boolean;
  /**
   * Пловец, ради которого пришли (`?swimmer=`) — подсвечиваем все его строки.
   * ⚠ Это НЕ «я»: адрес говорит лишь, с чьей страницы пришла ссылка, и любой может её
   * переслать. «Я» в продукте — это primary favorite залогиненного пользователя
   * (правило: привязка из URL недоверенная), и бейджа ★ ME здесь быть не должно.
   */
  highlightSwimmerId?: number | null;
}

function SbRow({ row, modules, stroke, distance, isMasters, highlightSwimmerId }: RowProps) {
  const isLinked = highlightSwimmerId != null && row.swimmer_id === highlightSwimmerId;
  const isRepeat = row.attempt > 1;
  // Имя как в протоколе — так его показывают и таблица результатов, и страница пловца.
  // Латиница доступна за флагом `latinNames`; поле `name_en` заполнено не у всех.
  const name = (modules.latinNames && row.name_en?.trim()) || row.name;
  // Клуб — тоже как в протоколе: по этому же имени `UI_ClubIcon` находит логотип
  // (файлы названы оригиналом), так что латинский вариант оставил бы строку без эмблемы.
  const club = (modules.latinNames && row.club_en?.trim()) || row.club || '';

  // Мягкая заливка по полу — та же семантика, что в таблице результатов. Красим ТОКЕНЫ
  // строки (`--sr-bg`/`--sr-border`), а не её background напрямую: строка читает цвета
  // только из токенов, и прямая заливка спорила бы с ними в зависимости от порядка CSS.
  const tint = modules.genderTint && (row.gender === 'male' || row.gender === 'female')
    ? ` sb-swim-row--${row.gender}`
    : '';

  return (
    <SwimRow
      className={
        `sb-swim-row${tint}`
        + (isLinked ? ' sb-swim-row--linked' : '')
        + (isRepeat ? ' sb-swim-row--repeat' : '')
      }
      href={routes.swimmer(row.swimmer_id)}
      stroke={stroke}
      distance={distance}
      poolType={row.pool_type}
      // Дисциплина одна на весь срез и уже названа шапкой и чипом Event — плитку в строке
      // держим флагом, чтобы её можно было вернуть одной правкой.
      showEvent={modules.disciplineInRow}
      time={row.time}
      quality={row.suspect_reason ? { kind: 'protocol', reason: row.suspect_reason } : null}
      // Первое место в срезе — это и есть season best, носится как рекорд (правило Влада).
      place={{ kind: 'rank', value: row.place, isFirst: row.place === 1 }}
      badge={row.place === 1 ? 'sb' : null}
      swimmer={{
        name,
        club: modules.clubInRow ? club : null,
        showClubIcon: modules.clubLogoInRow,
      }}
      competition={modules.meetInRow && row.competition ? { name: row.competition } : null}
      date={modules.dateInRow ? row.date : null}
      points={modules.pointsInRow ? row.points : null}
      gapMs={modules.gapInRow ? row.gap_ms : null}
      // Дуга уровня считается на клиенте из тех же данных, что и везде: пол, сезонный
      // возраст, бассейн, время. У мастерских срезов возрастная группа протокола («25-29»)
      // и есть признак мастерса — у них своя шкала нормативов.
      level={
        modules.levelInRow
          ? {
              gender: row.gender,
              ageInSeason: row.age,
              ageGroup: isMasters ? row.age_group ?? null : null,
              isMasters,
            }
          : null
      }
      extras={
        modules.attemptInRow && isRepeat
          ? <span className="sb-attempt">{attemptLabel(row.attempt)}</span>
          : null
      }
    />
  );
}

interface Props {
  rows: SeasonBestListItem[];
  modules: SeasonBestModules;
  stroke: string;
  distance: string;
  isMasters: boolean;
  highlightSwimmerId?: number | null;
}

function SbList({ rows, modules, stroke, distance, isMasters, highlightSwimmerId }: Props) {
  return (
    <div className="sb-list">
      {rows.map((row) => (
        <SbRow
          key={row.result_id}
          row={row}
          modules={modules}
          stroke={stroke}
          distance={distance}
          isMasters={isMasters}
          highlightSwimmerId={highlightSwimmerId}
        />
      ))}
    </div>
  );
}

export default SbList;
